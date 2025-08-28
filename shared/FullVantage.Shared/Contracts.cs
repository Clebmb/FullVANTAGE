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

// File Transfer Contracts
public record FileInfo
(
    string AgentId,
    string Name,
    string FullPath,
    long Size,
    bool IsDirectory,
    DateTimeOffset LastModified,
    string? Attributes,
    string? Owner
);

public record DirectoryListing
(
    string AgentId,
    string Path,
    IReadOnlyList<FileInfo> Files,
    IReadOnlyList<FileInfo> Directories,
    long TotalSize,
    int TotalCount
);

public record FileTransferRequest
(
    string TransferId,
    string AgentId,
    string SourcePath,
    string DestinationPath,
    FileTransferType Type,
    bool Overwrite,
    int ChunkSize
);

public record FileTransferChunk
(
    string TransferId,
    string AgentId,
    int ChunkIndex,
    int TotalChunks,
    byte[] Data,
    bool IsFinal
);

public record FileTransferProgress
(
    string TransferId,
    string AgentId,
    int ChunkIndex,
    int TotalChunks,
    long BytesTransferred,
    long TotalBytes,
    FileTransferStatus Status,
    string? ErrorMessage
);

public record FileOperationRequest
(
    string OperationId,
    string AgentId,
    FileOperationType Type,
    IReadOnlyList<string> SourcePaths,
    string? DestinationPath,
    bool Overwrite
);

public record FileOperationResult
(
    string OperationId,
    string AgentId,
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<string> AffectedPaths
);

public enum FileTransferType
{
    Upload,     // Server -> Agent
    Download    // Agent -> Server
}

public enum FileTransferStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public enum FileOperationType
{
    Copy,
    Move,
    Delete,
    Rename,
    CreateDirectory
}
