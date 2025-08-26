using System;
using System.Threading.Tasks;
using FullVantage.Server.Services;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR;

namespace FullVantage.Server.Hubs;

public class AgentHub : Hub
{
    private readonly AgentRegistry _registry;

    public AgentHub(AgentRegistry registry)
    {
        _registry = registry;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var agentId = _registry.GetAgentIdByConnection(Context.ConnectionId);
        if (agentId is not null)
        {
            _registry.MarkOffline(agentId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Register(AgentHello hello)
    {
        // Track agent and group by ID for targeted messaging
        _registry.Upsert(hello);
        _registry.MapConnection(Context.ConnectionId, hello.AgentId);
        await Groups.AddToGroupAsync(Context.ConnectionId, hello.AgentId);
        await Clients.Caller.SendAsync("Registered", hello.AgentId);
        Console.WriteLine($"Agent registered: {hello.AgentId} ({hello.MachineName} / {hello.UserName})");
    }

    public Task CommandOutput(CommandChunk chunk)
    {
        // Relay to any listening admin clients (optional: add a separate admin hub later)
        // For now, store in registry (last output) and no-op
        _registry.SetLastSeen(chunk.AgentId);
        return Task.CompletedTask;
    }
}
