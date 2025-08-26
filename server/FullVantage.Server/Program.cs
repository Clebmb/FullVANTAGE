using FullVantage.Server.Components;
using FullVantage.Server.Hubs;
using FullVantage.Server.Services;
using Microsoft.AspNetCore.SignalR;
using FullVantage.Shared;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);

// Ensure server listens on a shared dev URL so agent default matches
builder.WebHost.UseUrls(Defaults.DevServerUrl);

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

// List agents
app.MapGet("/api/agents", (AgentRegistry registry) => Results.Ok(registry.List()));

app.Run();
