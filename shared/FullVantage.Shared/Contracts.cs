using System;
using System.Collections.Generic;

namespace FullVantage.Shared;

public enum AgentStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
}

public record AgentHello
(
    string AgentId,
    string MachineName,
    string UserName,
    string OsVersion,
    string Version
);

public record AgentInfo
(
    string AgentId,
    string MachineName,
    string UserName,
    string OsVersion,
    DateTimeOffset LastSeenUtc,
    AgentStatus Status
);

public record CommandRequest
(
    string CommandId,
    string AgentId,
    string Shell, // "powershell"
    string ScriptOrCommand,
    TimeSpan? Timeout
);

public record CommandChunk
(
    string CommandId,
    string AgentId,
    string Stream, // stdout | stderr
    string Data,
    bool IsFinal
);

public record CommandOutput
(
    string CommandId,
    string AgentId,
    string Stream,
    string Data,
    DateTimeOffset Timestamp,
    bool IsFinal
);

public record SystemInfo
(
    string AgentId,
    string OS,
    string CPU,
    string Memory,
    IReadOnlyList<DiskInfo> Disks
);

public record DiskInfo(string Name, string Format, long TotalBytes, long FreeBytes);
