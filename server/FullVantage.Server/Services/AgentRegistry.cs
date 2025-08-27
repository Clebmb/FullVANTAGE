using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FullVantage.Shared;

namespace FullVantage.Server.Services;

public class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, string> _connectionToAgent = new();
    private readonly ConcurrentDictionary<string, List<CommandOutput>> _commandOutputs = new();

    public void Upsert(AgentHello hello)
    {
        var info = new AgentInfo(
            hello.AgentId,
            hello.MachineName,
            hello.UserName,
            hello.OsVersion,
            DateTimeOffset.UtcNow,
            AgentStatus.Online);
        _agents.AddOrUpdate(hello.AgentId, info, (_, __) => info);
    }

    public void MapConnection(string connectionId, string agentId)
    {
        _connectionToAgent[connectionId] = agentId;
    }

    public string? GetAgentIdByConnection(string connectionId)
    {
        return _connectionToAgent.TryGetValue(connectionId, out var agentId) ? agentId : null;
    }

    public void MarkOffline(string agentId)
    {
        if (_agents.TryGetValue(agentId, out var info))
        {
            var updated = info with { LastSeenUtc = DateTimeOffset.UtcNow, Status = AgentStatus.Offline };
            _agents[agentId] = updated;
        }
    }

    public void SetLastSeen(string agentId)
    {
        if (_agents.TryGetValue(agentId, out var info))
        {
            var updated = info with { LastSeenUtc = DateTimeOffset.UtcNow, Status = AgentStatus.Online };
            _agents[agentId] = updated;
        }
    }

    public void AddCommandOutput(string agentId, CommandOutput output)
    {
        if (!_commandOutputs.ContainsKey(agentId))
        {
            _commandOutputs[agentId] = new List<CommandOutput>();
        }
        _commandOutputs[agentId].Add(output);
        
        // Keep only last 100 outputs per agent
        if (_commandOutputs[agentId].Count > 100)
        {
            _commandOutputs[agentId].RemoveRange(0, _commandOutputs[agentId].Count - 100);
        }
    }

    public IReadOnlyCollection<AgentInfo> List() => _agents.Values.ToArray();
    
    public IReadOnlyCollection<CommandOutput> GetCommandOutputs(string agentId)
    {
        return _commandOutputs.TryGetValue(agentId, out var outputs) ? outputs.ToArray() : Array.Empty<CommandOutput>();
    }
}
