using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FullVantage.Server.Services;

public class FileTransferService
{
    private readonly ConcurrentDictionary<string, FileTransferSession> _activeTransfers = new();
    private readonly ConcurrentDictionary<string, FileOperationSession> _activeOperations = new();
    private readonly IHubContext<FullVantage.Server.Hubs.AgentHub> _hubContext;
    private readonly ILogger<FileTransferService> _logger;

    public FileTransferService(IHubContext<FullVantage.Server.Hubs.AgentHub> hubContext, ILogger<FileTransferService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<FileTransferRequest> InitiateTransferAsync(
        string agentId, 
        string sourcePath, 
        string destinationPath, 
        FileTransferType type, 
        bool overwrite = false)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var session = new FileTransferSession
        {
            TransferId = transferId,
            AgentId = agentId,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            Type = type,
            Overwrite = overwrite,
            Status = FileTransferStatus.Pending,
            StartTime = DateTimeOffset.UtcNow
        };

        _activeTransfers[transferId] = session;
        _logger.LogInformation("Initiated file transfer {TransferId} for agent {AgentId}: {Type} {Source} -> {Destination}", 
            transferId, agentId, type, sourcePath, destinationPath);

        return new FileTransferRequest(
            transferId, agentId, sourcePath, destinationPath, type, overwrite, 64 * 1024); // 64KB chunks
    }

    public async Task<FileOperationRequest> InitiateFileOperationAsync(
        string agentId,
        FileOperationType type,
        IReadOnlyList<string> sourcePaths,
        string? destinationPath = null,
        bool overwrite = false)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var session = new FileOperationSession
        {
            OperationId = operationId,
            AgentId = agentId,
            Type = type,
            SourcePaths = sourcePaths,
            DestinationPath = destinationPath,
            Overwrite = overwrite,
            Status = FileTransferStatus.Pending,
            StartTime = DateTimeOffset.UtcNow
        };

        _activeOperations[operationId] = session;
        _logger.LogInformation("Initiated file operation {OperationId} for agent {AgentId}: {Type}", 
            operationId, agentId, type);

        return new FileOperationRequest(operationId, agentId, type, sourcePaths, destinationPath, overwrite);
    }

    public async Task ProcessFileTransferChunkAsync(FileTransferChunk chunk)
    {
        if (!_activeTransfers.TryGetValue(chunk.TransferId, out var session))
        {
            _logger.LogWarning("Received chunk for unknown transfer {TransferId}", chunk.TransferId);
            return;
        }

        try
        {
            if (chunk.ChunkIndex == 0)
            {
                session.Status = FileTransferStatus.InProgress;
                session.BytesTransferred = 0;
                
                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(session.DestinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // Create or truncate destination file
                if (session.Overwrite || !File.Exists(session.DestinationPath))
                {
                    File.Create(session.DestinationPath).Dispose();
                }
            }

            // Write chunk to file
            using var stream = new FileStream(session.DestinationPath, FileMode.Open, FileAccess.Write);
            stream.Seek(chunk.ChunkIndex * session.ChunkSize, SeekOrigin.Begin);
            await stream.WriteAsync(chunk.Data, 0, chunk.Data.Length);
            await stream.FlushAsync();

            session.BytesTransferred += chunk.Data.Length;
            session.LastChunkIndex = chunk.ChunkIndex;

            // Update progress
            var progress = new FileTransferProgress(
                chunk.TransferId,
                chunk.AgentId,
                chunk.ChunkIndex,
                chunk.TotalChunks,
                session.BytesTransferred,
                session.TotalBytes,
                session.Status,
                null);

            await _hubContext.Clients.All.SendAsync("FileTransferProgress", progress);

            if (chunk.IsFinal)
            {
                session.Status = FileTransferStatus.Completed;
                session.EndTime = DateTimeOffset.UtcNow;
                
                var finalProgress = new FileTransferProgress(
                    chunk.TransferId,
                    chunk.AgentId,
                    chunk.ChunkIndex,
                    chunk.TotalChunks,
                    session.BytesTransferred,
                    session.TotalBytes,
                    session.Status,
                    null);

                await _hubContext.Clients.All.SendAsync("FileTransferProgress", finalProgress);
                
                _activeTransfers.TryRemove(chunk.TransferId, out _);
                _logger.LogInformation("File transfer {TransferId} completed successfully", chunk.TransferId);
            }
        }
        catch (Exception ex)
        {
            session.Status = FileTransferStatus.Failed;
            session.ErrorMessage = ex.Message;
            session.EndTime = DateTimeOffset.UtcNow;

            var errorProgress = new FileTransferProgress(
                chunk.TransferId,
                chunk.AgentId,
                chunk.ChunkIndex,
                chunk.TotalChunks,
                session.BytesTransferred,
                session.TotalBytes,
                session.Status,
                ex.Message);

            await _hubContext.Clients.All.SendAsync("FileTransferProgress", errorProgress);
            
            _activeTransfers.TryRemove(chunk.TransferId, out _);
            _logger.LogError(ex, "File transfer {TransferId} failed", chunk.TransferId);
        }
    }

    public async Task<FileOperationResult> ProcessFileOperationResultAsync(string operationId, bool success, string? errorMessage, IReadOnlyList<string> affectedPaths)
    {
        if (!_activeOperations.TryGetValue(operationId, out var session))
        {
            _logger.LogWarning("Received result for unknown operation {OperationId}", operationId);
            return new FileOperationResult(operationId, "", false, "Unknown operation", Array.Empty<string>());
        }

        session.Status = success ? FileTransferStatus.Completed : FileTransferStatus.Failed;
        session.EndTime = DateTimeOffset.UtcNow;

        var result = new FileOperationResult(operationId, session.AgentId, success, errorMessage, affectedPaths);
        
        await _hubContext.Clients.All.SendAsync("FileOperationCompleted", result);
        
        _activeOperations.TryRemove(operationId, out _);
        _logger.LogInformation("File operation {OperationId} completed with status: {Status}", operationId, session.Status);

        return result;
    }

    public IReadOnlyList<FileTransferSession> GetActiveTransfers()
    {
        return _activeTransfers.Values.ToList();
    }

    public IReadOnlyList<FileOperationSession> GetActiveOperations()
    {
        return _activeOperations.Values.ToList();
    }

    public bool CancelTransfer(string transferId)
    {
        if (_activeTransfers.TryGetValue(transferId, out var session))
        {
            session.Status = FileTransferStatus.Cancelled;
            session.EndTime = DateTimeOffset.UtcNow;
            _activeTransfers.TryRemove(transferId, out _);
            
            _logger.LogInformation("File transfer {TransferId} cancelled", transferId);
            return true;
        }
        return false;
    }

    public bool CancelOperation(string operationId)
    {
        if (_activeOperations.TryGetValue(operationId, out var session))
        {
            session.Status = FileTransferStatus.Cancelled;
            session.EndTime = DateTimeOffset.UtcNow;
            _activeOperations.TryRemove(operationId, out _);
            
            _logger.LogInformation("File operation {OperationId} cancelled", operationId);
            return true;
        }
        return false;
    }

    public class FileTransferSession
    {
        public string TransferId { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string DestinationPath { get; set; } = "";
        public FileTransferType Type { get; set; }
        public bool Overwrite { get; set; }
        public int ChunkSize { get; set; } = 64 * 1024;
        public FileTransferStatus Status { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public int LastChunkIndex { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
    }

    public class FileOperationSession
    {
        public string OperationId { get; set; } = "";
        public string AgentId { get; set; } = "";
        public FileOperationType Type { get; set; }
        public IReadOnlyList<string> SourcePaths { get; set; } = Array.Empty<string>();
        public string? DestinationPath { get; set; }
        public bool Overwrite { get; set; }
        public FileTransferStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
    }
}
