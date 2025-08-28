using System;
using System.Threading.Tasks;
using FullVantage.Server.Services;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR;

namespace FullVantage.Server.Hubs;

public class AgentHub : Hub
{
    private readonly AgentRegistry _registry;
    private readonly FileTransferService _fileTransferService;
    private readonly FileBrowserService _fileBrowserService;

    public AgentHub(AgentRegistry registry, FileTransferService fileTransferService, FileBrowserService fileBrowserService)
    {
        _registry = registry;
        _fileTransferService = fileTransferService;
        _fileBrowserService = fileBrowserService;
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
        Console.WriteLine($"[SignalR] Register called for agent: {hello.AgentId} ({hello.MachineName} / {hello.UserName})");
        Console.WriteLine($"[SignalR] Connection ID: {Context.ConnectionId}");
        
        // Track agent and group by ID for targeted messaging
        _registry.Upsert(hello);
        _registry.MapConnection(Context.ConnectionId, hello.AgentId);
        
        Console.WriteLine($"[SignalR] Adding connection {Context.ConnectionId} to group {hello.AgentId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, hello.AgentId);
        
        await Clients.Caller.SendAsync("Registered", hello.AgentId);
        Console.WriteLine($"[SignalR] Agent registered successfully: {hello.AgentId} ({hello.MachineName} / {hello.UserName})");
        Console.WriteLine($"[SignalR] Agent is now in group: {hello.AgentId}");
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

    // File Transfer Methods
    public async Task FileTransferChunk(FileTransferChunk chunk)
    {
        await _fileTransferService.ProcessFileTransferChunkAsync(chunk);
    }

    public async Task FileOperationResult(FileOperationResult result)
    {
        await _fileTransferService.ProcessFileOperationResultAsync(
            result.OperationId, 
            result.Success, 
            result.ErrorMessage, 
            result.AffectedPaths);
    }

    public async Task DirectoryListingRequest(string agentId, string path)
    {
        // Request directory listing from agent
        Console.WriteLine($"[SignalR] DirectoryListingRequest called for agent {agentId} at path {path}");
        Console.WriteLine($"[SignalR] Sending GetDirectoryListing to group {agentId}");
        await Clients.Group(agentId).SendAsync("GetDirectoryListing", path);
        Console.WriteLine($"[SignalR] GetDirectoryListing sent to group {agentId}");
    }

    public async Task FileInfoRequest(string agentId, string path)
    {
        // Request file info from agent
        await Clients.Group(agentId).SendAsync("GetFileInfo", path);
    }

    public async Task DirectoryListingResponse(DirectoryListing listing)
    {
        Console.WriteLine($"[SignalR] Received DirectoryListingResponse from agent {listing.AgentId} for path {listing.Path}");
        Console.WriteLine($"[SignalR] Files: {listing.Files.Count}, Directories: {listing.Directories.Count}");
        
        // Store directory listing in the browser service
        _fileBrowserService.SetDirectoryListing(listing.AgentId, listing.Path, listing);
        Console.WriteLine($"[SignalR] Directory listing stored in FileBrowserService");
        
        // Broadcast directory listing to all admin clients
        await Clients.All.SendAsync("DirectoryListingReceived", listing);
        Console.WriteLine($"[SignalR] Directory listing broadcasted to admin clients");
    }

    public async Task FileInfoResponse(FullVantage.Shared.FileInfo fileInfo)
    {
        // Store file info in the browser service
        _fileBrowserService.SetFileInfo(fileInfo.AgentId, fileInfo.FullPath, fileInfo);
        
        // Broadcast file info to all admin clients
        await Clients.All.SendAsync("FileInfoReceived", fileInfo);
    }
}
