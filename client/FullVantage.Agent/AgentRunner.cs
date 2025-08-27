using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace FullVantage.Agent;

public class AgentRunner
{
    private HubConnection? _connection;
    private readonly string _agentId = Guid.NewGuid().ToString("N");
    
    // Events for UI updates
    public event EventHandler<CommandRequest>? CommandReceived;
    public event EventHandler<CommandChunk>? CommandOutputReceived;
    public event EventHandler<AgentStatus>? StatusChanged;
    
    // Public properties
    public string AgentId => _agentId;
    public AgentStatus CurrentStatus { get; private set; } = AgentStatus.Unknown;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Resolve server URL from local config file, env var, or default
        var serverUrl = TryLoadConfigServerUrl()
            ?? Environment.GetEnvironmentVariable("FULLVANTAGE_SERVER")
            ?? FullVantage.Shared.Defaults.DevServerUrl;
        // Normalize
        if (serverUrl.EndsWith("/", StringComparison.Ordinal)) serverUrl = serverUrl.TrimEnd('/');

        // Persist the resolved URL for troubleshooting
        try
        {
            var usedPath = Path.Combine(AppContext.BaseDirectory, "agent.used.url.txt");
            File.WriteAllText(usedPath, $"{DateTimeOffset.Now:u} -> {serverUrl}{Environment.NewLine}");
        }
        catch { }
        var hubUrl = new Uri(new Uri(serverUrl), "/hubs/agent").ToString();

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<CommandRequest>("ExecuteCommand", async (req) =>
        {
            if (req.AgentId != _agentId && !string.IsNullOrEmpty(req.AgentId))
            {
                // Not for this agent
                return;
            }

            // Notify UI that command was received
            CommandReceived?.Invoke(this, req);
            
            await RunPowerShellAndStreamAsync(req);
        });

        _connection.Reconnected += async (_) =>
        {
            await RegisterAsync();
        };

        _connection.Closed += async (_) =>
        {
            UpdateStatus(AgentStatus.Offline);
        };

        try
        {
            await _connection.StartAsync(cancellationToken);
            UpdateStatus(AgentStatus.Online);
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus(AgentStatus.Offline);
            // Log error or show in UI
            Console.WriteLine($"Failed to connect: {ex.Message}");
        }
    }

    private void UpdateStatus(AgentStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    private async Task RegisterAsync()
    {
        if (_connection is null) return;
        var hello = new AgentHello(
            _agentId,
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.ToString(),
            Version: typeof(AgentRunner).Assembly.GetName().Version?.ToString() ?? "1.0.0");
        await _connection.InvokeAsync("Register", hello);
    }

    private async Task RunPowerShellAndStreamAsync(CommandRequest req)
    {
        if (_connection is null) return;
        var timeout = req.Timeout ?? TimeSpan.FromMinutes(2);
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            using var ps = PowerShell.Create();
            
            // Try to suppress snap-in loading errors by setting error action preference
            ps.AddScript("$ErrorActionPreference = 'SilentlyContinue'");
            ps.Invoke();
            ps.Commands.Clear();
            
            ps.AddScript(req.ScriptOrCommand);

            ps.Streams.Error.DataAdded += async (s, e) =>
            {
                var rec = ((PSDataCollection<ErrorRecord>)s!)[e.Index];
                var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", rec.ToString() ?? string.Empty, false);
                
                // Notify UI of output
                CommandOutputReceived?.Invoke(this, chunk);
                
                await SafeSendAsync(chunk);
            };

            Console.WriteLine("Executing PowerShell command...");
            var output = await Task.Run(() => ps.Invoke(), cts.Token);
            Console.WriteLine($"PowerShell execution completed. Output count: {output.Count}");
            
            foreach (var item in output)
            {
                var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", item?.ToString() ?? string.Empty, false);
                
                // Notify UI of output
                CommandOutputReceived?.Invoke(this, chunk);
                
                await SafeSendAsync(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", "Command timed out.", false);
            
            // Notify UI of output
            CommandOutputReceived?.Invoke(this, chunk);
            
            await SafeSendAsync(chunk);
        }
        catch (Exception ex)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", ex.ToString(), false);
            
            // Notify UI of output
            CommandOutputReceived?.Invoke(this, chunk);
            
            await SafeSendAsync(chunk);
        }
        finally
        {
            var final = new CommandChunk(req.CommandId, _agentId, "stdout", string.Empty, true);
            
            // Notify UI of final output
            CommandOutputReceived?.Invoke(this, final);
            
            await SafeSendAsync(final);
        }
    }

    private Task SafeSendAsync(CommandChunk chunk)
    {
        try
        {
            return _connection!.InvokeAsync("CommandOutput", chunk);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private static string? TryLoadConfigServerUrl()
    {
        try
        {
            // Look for agent.config.json next to the executable (legacy support)
            var exeDir = AppContext.BaseDirectory;
            var cfgPath = Path.Combine(exeDir, "agent.config.json");
            if (File.Exists(cfgPath))
            {
                var json = File.ReadAllText(cfgPath);
                var cfg = JsonSerializer.Deserialize<AgentConfig>(json);
                var url = cfg?.ServerUrl?.Trim();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }

            // Check for a simple .txt file with just the URL (simpler approach)
            var txtPath = Path.Combine(exeDir, "server.txt");
            if (File.Exists(txtPath))
            {
                var url = File.ReadAllText(txtPath).Trim();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record AgentConfig(string? ServerUrl);
}
