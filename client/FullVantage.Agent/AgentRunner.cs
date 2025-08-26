using System;
using System.Diagnostics;
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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Make configurable via builder; for now, fallback to env var FULLVANTAGE_SERVER or default https://localhost:5001
        var serverUrl = Environment.GetEnvironmentVariable("FULLVANTAGE_SERVER") ?? FullVantage.Shared.Defaults.DevServerUrl;
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

            await RunPowerShellAndStreamAsync(req);
        });

        _connection.Reconnected += async (_) =>
        {
            await RegisterAsync();
        };

        await _connection.StartAsync(cancellationToken);
        await RegisterAsync();
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
            ps.AddScript(req.ScriptOrCommand);

            ps.Streams.Error.DataAdded += async (s, e) =>
            {
                var rec = ((PSDataCollection<ErrorRecord>)s!)[e.Index];
                var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", rec.ToString() ?? string.Empty, false);
                await SafeSendAsync(chunk);
            };

            var output = await Task.Run(() => ps.Invoke(), cts.Token);
            foreach (var item in output)
            {
                var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", item?.ToString() ?? string.Empty, false);
                await SafeSendAsync(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", "Command timed out.", false);
            await SafeSendAsync(chunk);
        }
        catch (Exception ex)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", ex.ToString(), false);
            await SafeSendAsync(chunk);
        }
        finally
        {
            var final = new CommandChunk(req.CommandId, _agentId, "stdout", string.Empty, true);
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
}
