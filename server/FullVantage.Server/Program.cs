using FullVantage.Server.Components;
using FullVantage.Server.Hubs;
using FullVantage.Server.Services;
using Microsoft.AspNetCore.SignalR;
using FullVantage.Shared;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// ALWAYS prompt for bind address/port before starting, using config/env as defaults if present.
var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var configUrl = builder.Configuration["Server:Url"];
var basis = !string.IsNullOrWhiteSpace(configUrl) ? configUrl!
    : (!string.IsNullOrWhiteSpace(envUrls) ? envUrls! : Defaults.DevServerUrl);
var basisUrl = basis.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? Defaults.DevServerUrl;
var parsed = new Uri(basisUrl);
var defaultHost = parsed.Host;
var defaultPort = parsed.Port;

Console.Write($"Server Address [{defaultHost}]: ");
var hostInput = Console.ReadLine();
var host = string.IsNullOrWhiteSpace(hostInput) ? defaultHost : hostInput.Trim();

Console.Write($"Server Port [{defaultPort}]: ");
var portInput = Console.ReadLine();
int port = defaultPort;
if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out var p) && p > 0 && p < 65536)
{
    port = p;
}

var finalUrl = $"http://{host}:{port}";
Console.WriteLine($"\nStarting server on {finalUrl} ...\n");

builder.WebHost.UseUrls(finalUrl);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR + registries
builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Hubs
app.MapHub<AgentHub>("/hubs/agent");

// Minimal API for admin actions
app.MapPost("/api/agents/{agentId}/commands", async (string agentId, AgentRegistry registry, Microsoft.AspNetCore.SignalR.IHubContext<AgentHub> hub, FullVantage.Shared.CommandRequest req) =>
{
    // Normalize
    var command = req with { AgentId = agentId };
    // Route to agent group
    await hub.Clients.Group(agentId).SendAsync("ExecuteCommand", command);
    return Results.Accepted($"/api/agents/{agentId}/commands/{command.CommandId}");
})
.DisableAntiforgery();

// Get current server bind info (reflect the URL chosen at startup)
app.MapGet("/api/server-info", () => Results.Ok(new { Url = finalUrl }));

// Save server bind URL to appsettings.json (requires manual restart)
app.MapPost("/api/server-config", async ([FromBody] ServerConfigRequest req, IWebHostEnvironment env) =>
{
    var scheme = string.IsNullOrWhiteSpace(req.Scheme) ? "http" : req.Scheme.Trim();
    var host = string.IsNullOrWhiteSpace(req.Host) ? "localhost" : req.Host.Trim();
    var port = req.Port <= 0 ? new Uri(Defaults.DevServerUrl).Port : req.Port;
    var url = $"{scheme}://{host}:{port}";

    var appsettings = Path.Combine(env.ContentRootPath, "appsettings.json");
    JsonObject root;
    if (File.Exists(appsettings))
    {
        var json = await File.ReadAllTextAsync(appsettings);
        root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }
    else
    {
        root = new JsonObject();
    }

    root["Server"] = new JsonObject { ["Url"] = url };
    var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(appsettings, updated);
    return Results.Ok(new { Url = url, Note = "Server settings saved. Please restart the server to apply." });
}).DisableAntiforgery();

// GET variant for direct download via navigation
app.MapGet("/api/build-agent", async (string host, int port, string? scheme, IWebHostEnvironment env) =>
{
    var schemeVal = string.IsNullOrWhiteSpace(scheme) ? "http" : scheme;
    var hostVal = string.IsNullOrWhiteSpace(host) ? "localhost" : host;
    var portVal = port <= 0 ? new Uri(Defaults.DevServerUrl).Port : port;
    var serverUrl = $"{schemeVal}://{hostVal}:{portVal}";

    var contentRoot = env.ContentRootPath;
    var clientProj = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "client", "FullVantage.Agent", "FullVantage.Agent.csproj"));
    if (!File.Exists(clientProj)) return Results.Problem($"Client project not found at {clientProj}");

    var workDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fullvantage-build", Guid.NewGuid().ToString("N")));
    var publishDir = Path.Combine(workDir.FullName, "publish");

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"publish \"{clientProj}\" -c Release -o \"{publishDir}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    var proc = Process.Start(psi)!;
    var stdout = await proc.StandardOutput.ReadToEndAsync();
    var stderr = await proc.StandardError.ReadToEndAsync();
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0)
    {
        return Results.Problem($"dotnet publish failed: {stderr}\n{stdout}");
    }

    var cfgPath = Path.Combine(publishDir, "agent.config.json");
    await File.WriteAllTextAsync(cfgPath, JsonSerializer.Serialize(new { ServerUrl = serverUrl }, new JsonSerializerOptions { WriteIndented = true }));

    // Convenience launcher that sets env var and runs the agent
    var runCmd = Path.Combine(publishDir, "RunAgent.cmd");
    var cmd = "@echo off\r\n" +
              $"set \"FULLVANTAGE_SERVER={serverUrl}\"\r\n" +
              "start \"\" \"%~dp0FullVantage.Agent.exe\"\r\n";
    await File.WriteAllTextAsync(runCmd, cmd);

    var zipPath = Path.Combine(workDir.FullName, "FullVantage.Agent.zip");
    ZipFile.CreateFromDirectory(publishDir, zipPath);
    var fs = File.OpenRead(zipPath);
    return Results.File(fs, "application/zip", "FullVantage.Agent.zip");
}).DisableAntiforgery();

// List agents
app.MapGet("/api/agents", (AgentRegistry registry) => Results.Ok(registry.List()));

// Client builder: publish agent with embedded config and return zip
app.MapPost("/api/build-agent", async ([FromBody] BuildRequest req, IWebHostEnvironment env) =>
{
    var scheme = string.IsNullOrWhiteSpace(req.Scheme) ? "http" : req.Scheme;
    var host = string.IsNullOrWhiteSpace(req.Host) ? "localhost" : req.Host;
    var port = req.Port <= 0 ? new Uri(Defaults.DevServerUrl).Port : req.Port;
    var serverUrl = $"{scheme}://{host}:{port}";

    // Paths
    var contentRoot = env.ContentRootPath; // server/FullVantage.Server
    var clientProj = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "client", "FullVantage.Agent", "FullVantage.Agent.csproj"));
    if (!File.Exists(clientProj)) return Results.Problem($"Client project not found at {clientProj}");

    var workDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fullvantage-build", Guid.NewGuid().ToString("N")));
    var publishDir = Path.Combine(workDir.FullName, "publish");

    // Publish agent
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"publish \"{clientProj}\" -c Release -o \"{publishDir}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    var proc = Process.Start(psi)!;
    var stdout = await proc.StandardOutput.ReadToEndAsync();
    var stderr = await proc.StandardError.ReadToEndAsync();
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0)
    {
        return Results.Problem($"dotnet publish failed: {stderr}\n{stdout}");
    }

    // Write agent.config.json
    var cfgPath = Path.Combine(publishDir, "agent.config.json");
    await File.WriteAllTextAsync(cfgPath, JsonSerializer.Serialize(new { ServerUrl = serverUrl }, new JsonSerializerOptions { WriteIndented = true }));

    // Convenience launcher that sets env var and runs the agent
    var runCmd = Path.Combine(publishDir, "RunAgent.cmd");
    var cmd = "@echo off\r\n" +
              $"set \"FULLVANTAGE_SERVER={serverUrl}\"\r\n" +
              "start \"\" \"%~dp0FullVantage.Agent.exe\"\r\n";
    await File.WriteAllTextAsync(runCmd, cmd);

    // Zip output
    var zipPath = Path.Combine(workDir.FullName, "FullVantage.Agent.zip");
    ZipFile.CreateFromDirectory(publishDir, zipPath);
    var fs = File.OpenRead(zipPath);
    return Results.File(fs, "application/zip", "FullVantage.Agent.zip");
})
.DisableAntiforgery();

app.Run();

public record BuildRequest(string Host, int Port, string? Scheme);
public record ServerConfigRequest(string Host, int Port, string? Scheme);
