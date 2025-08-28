using System.Collections.Concurrent;
using FullVantage.Shared;
using FileInfo = FullVantage.Shared.FileInfo;

namespace FullVantage.Server.Services;

public class FileBrowserService
{
    private readonly ConcurrentDictionary<string, DirectoryListing> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, FileInfo> _pendingFileInfoRequests = new();
    private readonly ConcurrentDictionary<string, int> _accessCounts = new();

    public void SetDirectoryListing(string agentId, string path, DirectoryListing listing)
    {
        var key = $"{agentId}:{path}";
        Console.WriteLine($"[FileBrowserService] Storing directory listing for key: {key}");
        _pendingRequests[key] = listing;
        Console.WriteLine($"[FileBrowserService] Directory listing stored. Total pending: {_pendingRequests.Count}");
    }

    public void SetFileInfo(string agentId, string path, FileInfo fileInfo)
    {
        var key = $"{agentId}:{path}";
        Console.WriteLine($"[FileBrowserService] Storing file info for key: {key}");
        _pendingFileInfoRequests[key] = fileInfo;
        Console.WriteLine($"[FileBrowserService] File info stored. Total pending: {_pendingFileInfoRequests.Count}");
    }

    public DirectoryListing? GetDirectoryListing(string agentId, string path)
    {
        var key = $"{agentId}:{path}";
        Console.WriteLine($"[FileBrowserService] Looking for directory listing with key: {key}");
        Console.WriteLine($"[FileBrowserService] Available keys: {string.Join(", ", _pendingRequests.Keys)}");
        
        if (_pendingRequests.TryGetValue(key, out var listing))
        {
            Console.WriteLine($"[FileBrowserService] Found directory listing for key: {key}");
            
            // Track access count and remove after 3 accesses to prevent memory leaks
            var accessCount = _accessCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
            Console.WriteLine($"[FileBrowserService] Access count for {key}: {accessCount}");
            
            if (accessCount >= 3)
            {
                _pendingRequests.TryRemove(key, out _);
                _accessCounts.TryRemove(key, out _);
                Console.WriteLine($"[FileBrowserService] Removed directory listing after {accessCount} accesses: {key}");
            }
            
            return listing;
        }
        
        Console.WriteLine($"[FileBrowserService] No directory listing found for key: {key}");
        return null;
    }

    public FileInfo? GetFileInfo(string agentId, string path)
    {
        var key = $"{agentId}:{path}";
        Console.WriteLine($"[FileBrowserService] Looking for file info with key: {key}");
        Console.WriteLine($"[FileBrowserService] Available file info keys: {string.Join(", ", _pendingFileInfoRequests.Keys)}");
        
        if (_pendingFileInfoRequests.TryGetValue(key, out var fileInfo))
        {
            Console.WriteLine($"[FileBrowserService] Found file info for key: {key}");
            
            // Track access count and remove after 3 accesses to prevent memory leaks
            var accessCount = _accessCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
            Console.WriteLine($"[FileBrowserService] Access count for {key}: {accessCount}");
            
            if (accessCount >= 3)
            {
                _pendingFileInfoRequests.TryRemove(key, out _);
                _accessCounts.TryRemove(key, out _);
                Console.WriteLine($"[FileBrowserService] Removed file info after {accessCount} accesses: {key}");
            }
            
            return fileInfo;
        }
        
        Console.WriteLine($"[FileBrowserService] No file info found for key: {key}");
        return null;
    }

    public void ClearPendingRequests(string agentId)
    {
        var keysToRemove = _pendingRequests.Keys.Where(k => k.StartsWith($"{agentId}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _pendingRequests.TryRemove(key, out _);
        }

        var fileInfoKeysToRemove = _pendingFileInfoRequests.Keys.Where(k => k.StartsWith($"{agentId}:")).ToList();
        foreach (var key in fileInfoKeysToRemove)
        {
            _pendingFileInfoRequests.TryRemove(key, out _);
        }
    }

    public void ClearOldData(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow.Subtract(maxAge);
        
        // For now, we'll just clear all data older than the specified age
        // In a production system, you might want to add timestamps to track when data was stored
        var keysToRemove = _pendingRequests.Keys.ToList();
        foreach (var key in keysToRemove)
        {
            _pendingRequests.TryRemove(key, out _);
        }

        var fileInfoKeysToRemove = _pendingFileInfoRequests.Keys.ToList();
        foreach (var key in fileInfoKeysToRemove)
        {
            _pendingFileInfoRequests.TryRemove(key, out _);
        }
        
        Console.WriteLine($"[FileBrowserService] Cleared old data. Total pending: {_pendingRequests.Count}, Total file info: {_pendingFileInfoRequests.Count}");
    }

    public void ClearDataForAgent(string agentId, string path)
    {
        var key = $"{agentId}:{path}";
        _pendingRequests.TryRemove(key, out _);
        _pendingFileInfoRequests.TryRemove(key, out _);
        Console.WriteLine($"[FileBrowserService] Cleared data for key: {key}");
    }

    public void ClearAllData()
    {
        _pendingRequests.Clear();
        _pendingFileInfoRequests.Clear();
        Console.WriteLine($"[FileBrowserService] Cleared all data");
    }
}
