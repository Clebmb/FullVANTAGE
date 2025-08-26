using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FullVantage.Shared;

namespace FullVantage.Server.Services;

public class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, string> _connectionToAgent = new();

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

    public IReadOnlyCollection<AgentInfo> List() => _agents.Values.ToArray();
}
