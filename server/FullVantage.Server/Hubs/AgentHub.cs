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

    public Task Heartbeat(string agentId)
    {
        // Update last seen timestamp for the agent
        _registry.SetLastSeen(agentId);
        Console.WriteLine($"Heartbeat received from agent: {agentId}");
        return Task.CompletedTask;
    }

    public Task CommandOutput(CommandChunk chunk)
    {
        // Store command output in registry
        var output = new CommandOutput(
            chunk.CommandId,
            chunk.AgentId,
            chunk.Stream,
            chunk.Data,
            DateTimeOffset.UtcNow,
            chunk.IsFinal);
        
        _registry.AddCommandOutput(chunk.AgentId, output);
        _registry.SetLastSeen(chunk.AgentId);
        
        // Broadcast to all admin clients
        return Clients.All.SendAsync("CommandOutputReceived", output);
    }
}
