using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
            // Use direct process execution to avoid PowerShell SDK issues
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{req.ScriptOrCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            
            // Set up output collection
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", e.Data, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    _ = SafeSendAsync(chunk);
                }
            };
            
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", e.Data, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    _ = SafeSendAsync(chunk);
                }
            };

            Console.WriteLine("Executing PowerShell command...");
            
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start PowerShell process");
            }
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Wait for completion with timeout
            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cts.Token);
            
            if (!completed)
            {
                try { process.Kill(); } catch { }
                throw new OperationCanceledException("Command timed out");
            }
            
            Console.WriteLine($"PowerShell execution completed. Exit code: {process.ExitCode}");
            
            // Send any remaining output
            if (outputBuilder.Length > 0)
            {
                var output = outputBuilder.ToString().TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(output))
                {
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", output, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    await SafeSendAsync(chunk);
                }
            }
            
            if (errorBuilder.Length > 0)
            {
                var error = errorBuilder.ToString().TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(error))
                {
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", error, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    await SafeSendAsync(chunk);
                }
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
