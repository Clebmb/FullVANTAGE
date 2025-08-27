using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using System.Linq;

namespace FullVantage.Agent.Console;

public class AgentRunner
{
    private HubConnection? _connection;
    private readonly string _agentId = Guid.NewGuid().ToString("N");
    private readonly Timer _heartbeatTimer;
    private bool _isConnected = false;

    public AgentRunner()
    {
        _heartbeatTimer = new Timer(HeartbeatCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

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
        System.Console.WriteLine($"Connecting to SignalR hub at: {hubUrl}");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new CustomRetryPolicy())
            .Build();

        _connection.On<CommandRequest>("ExecuteCommand", async (req) =>
        {
            System.Console.WriteLine($"Received command: {req.CommandId} - {req.ScriptOrCommand}");
            if (req.AgentId != _agentId && !string.IsNullOrEmpty(req.AgentId))
            {
                System.Console.WriteLine($"Command not for this agent. Expected: {_agentId}, Got: {req.AgentId}");
                return;
            }

            await RunPowerShellAndStreamAsync(req);
        });

        _connection.Reconnected += async (connectionId) =>
        {
            System.Console.WriteLine($"Reconnected to server with connection ID: {connectionId}");
            _isConnected = true;
            await RegisterAsync();
            StartHeartbeat();
        };

        _connection.Closed += (exception) =>
        {
            System.Console.WriteLine($"Connection closed: {exception?.Message ?? "No error"}");
            _isConnected = false;
            StopHeartbeat();
            return Task.CompletedTask;
        };

        _connection.Reconnecting += (exception) =>
        {
            System.Console.WriteLine($"Reconnecting to server: {exception?.Message ?? "No error"}");
            _isConnected = false;
            StopHeartbeat();
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(cancellationToken);
            System.Console.WriteLine("Successfully connected to server");
            _isConnected = true;
            await RegisterAsync();
            StartHeartbeat();
            
            // Keep the application running
            while (!cancellationToken.IsCancellationRequested && _isConnected)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Failed to connect: {ex.Message}");
            throw;
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async void HeartbeatCallback(object? state)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("Heartbeat", _agentId);
                System.Console.WriteLine($"Heartbeat sent at {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Heartbeat failed: {ex.Message}");
            }
        }
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
        System.Console.WriteLine($"Registered with server as agent: {_agentId}");
    }

    private async Task RunPowerShellAndStreamAsync(CommandRequest req)
    {
        if (_connection is null) return;
        var timeout = req.Timeout ?? TimeSpan.FromMinutes(2);
        using var cts = new CancellationTokenSource(timeout);

        System.Console.WriteLine($"Executing PowerShell command: {req.ScriptOrCommand}");

        try
        {
            // Create a PowerShell execution context with error handling for snap-in loading
            using var ps = PowerShell.Create();
            
            // Try to suppress snap-in loading errors by setting error action preference
            ps.AddScript("$ErrorActionPreference = 'SilentlyContinue'");
            ps.Invoke();
            ps.Commands.Clear();
            
            // Add the actual command directly
            ps.AddScript(req.ScriptOrCommand);

            ps.Streams.Error.DataAdded += async (s, e) =>
            {
                var rec = ((PSDataCollection<ErrorRecord>)s!)[e.Index];
                var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", rec.ToString() ?? string.Empty, false);
                System.Console.WriteLine($"STDERR: {rec}");
                await SafeSendAsync(chunk);
            };

            System.Console.WriteLine("Executing PowerShell command...");
            var output = await Task.Run(() => ps.Invoke(), cts.Token);
            System.Console.WriteLine($"PowerShell execution completed. Output count: {output.Count}");
            
            foreach (var item in output)
            {
                var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", item?.ToString() ?? string.Empty, false);
                System.Console.WriteLine($"STDOUT: {item}");
                await SafeSendAsync(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", "Command timed out.", false);
            System.Console.WriteLine("Command timed out");
            await SafeSendAsync(chunk);
        }
        catch (Exception ex)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", ex.ToString(), false);
            System.Console.WriteLine($"Command failed: {ex.Message}");
            await SafeSendAsync(chunk);
        }
        finally
        {
            var final = new CommandChunk(req.CommandId, _agentId, "stdout", string.Empty, true);
            System.Console.WriteLine("Command execution completed");
            await SafeSendAsync(final);
        }
    }

    private Task SafeSendAsync(CommandChunk chunk)
    {
        try
        {
            return _connection!.InvokeAsync("CommandOutput", chunk);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Failed to send command output: {ex.Message}");
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

public class CustomRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.PreviousRetryCount >= 5)
            return null; // Stop retrying after 5 attempts

        return TimeSpan.FromSeconds(Math.Pow(2, retryContext.PreviousRetryCount)); // Exponential backoff
    }
}
