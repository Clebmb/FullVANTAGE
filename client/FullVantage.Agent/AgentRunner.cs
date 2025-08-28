using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FullVantage.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;

namespace FullVantage.Agent;

public class AgentRunner
{
    private HubConnection? _connection;
    private readonly string _agentId = Guid.NewGuid().ToString("N");
    
    // Events for UI updates
    public event EventHandler<CommandRequest>? CommandReceived;
    public event EventHandler<CommandChunk>? CommandOutputReceived;
    public event EventHandler<AgentStatus>? StatusChanged;
    public event EventHandler<FileTransferRequest>? FileTransferRequested;
    public event EventHandler<FileOperationRequest>? FileOperationRequested;
    public event EventHandler<string>? DirectoryListingRequested;
    public event EventHandler<string>? FileInfoRequested;
    
    // Public properties
    public string AgentId => _agentId;
    public AgentStatus CurrentStatus { get; private set; } = AgentStatus.Unknown;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Resolve server URL from local config file, env var, or default
        var serverUrl = TryLoadConfigServerUrl()
            ?? Environment.GetEnvironmentVariable("FULLVANTAGE_SERVER")
            ?? FullVantage.Shared.Defaults.DevServerUrl;
        // Normalize
        if (serverUrl.EndsWith("/", StringComparison.Ordinal)) serverUrl = serverUrl.TrimEnd('/');

        // Persist the resolved URL for troubleshooting
        try
        {
            var usedPath = Path.Combine(AppContext.BaseDirectory, "agent.used.url.txt");
            File.WriteAllText(usedPath, $"{DateTimeOffset.Now:u} -> {serverUrl}{Environment.NewLine}");
        }
        catch { }
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

            // Notify UI that command was received
            CommandReceived?.Invoke(this, req);
            
            await RunPowerShellAndStreamAsync(req);
        });

        // File Transfer Handlers
        _connection.On<FileTransferRequest>("FileTransferRequested", async (req) =>
        {
            if (req.AgentId != _agentId) return;
            
            FileTransferRequested?.Invoke(this, req);
            await HandleFileTransferAsync(req);
        });

        _connection.On<FileOperationRequest>("FileOperationRequested", async (req) =>
        {
            if (req.AgentId != _agentId) return;
            
            FileOperationRequested?.Invoke(this, req);
            await HandleFileOperationAsync(req);
        });

        _connection.On<string>("GetDirectoryListing", async (path) =>
        {
            Console.WriteLine($"Received GetDirectoryListing request for path: {path}");
            DirectoryListingRequested?.Invoke(this, path);
            await SendDirectoryListingAsync(path);
        });

        _connection.On<string>("GetFileInfo", async (path) =>
        {
            FileInfoRequested?.Invoke(this, path);
            await SendFileInfoAsync(path);
        });

        _connection.Reconnected += async (_) =>
        {
            await RegisterAsync();
        };

        _connection.Closed += async (_) =>
        {
            UpdateStatus(AgentStatus.Offline);
        };

        try
        {
            await _connection.StartAsync(cancellationToken);
            UpdateStatus(AgentStatus.Online);
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus(AgentStatus.Offline);
            // Log error or show in UI
            Console.WriteLine($"Failed to connect: {ex.Message}");
        }
    }

    private void UpdateStatus(AgentStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, status);
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
            // Use direct process execution to avoid PowerShell SDK issues
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{req.ScriptOrCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            
            // Set up output collection
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", e.Data, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    _ = SafeSendAsync(chunk);
                }
            };
            
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", e.Data, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    _ = SafeSendAsync(chunk);
                }
            };

            Console.WriteLine("Executing PowerShell command...");
            
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start PowerShell process");
            }
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Wait for completion with timeout
            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cts.Token);
            
            if (!completed)
            {
                try { process.Kill(); } catch { }
                throw new OperationCanceledException("Command timed out");
            }
            
            Console.WriteLine($"PowerShell execution completed. Exit code: {process.ExitCode}");
            
            // Send any remaining output
            if (outputBuilder.Length > 0)
            {
                var output = outputBuilder.ToString().TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(output))
                {
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stdout", output, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    await SafeSendAsync(chunk);
                }
            }
            
            if (errorBuilder.Length > 0)
            {
                var error = errorBuilder.ToString().TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(error))
                {
                    var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", error, false);
                    CommandOutputReceived?.Invoke(this, chunk);
                    await SafeSendAsync(chunk);
                }
            }
        }
        catch (OperationCanceledException)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", "Command timed out.", false);
            
            // Notify UI of output
            CommandOutputReceived?.Invoke(this, chunk);
            
            await SafeSendAsync(chunk);
        }
        catch (Exception ex)
        {
            var chunk = new CommandChunk(req.CommandId, _agentId, "stderr", ex.ToString(), false);
            
            // Notify UI of output
            CommandOutputReceived?.Invoke(this, chunk);
            
            await SafeSendAsync(chunk);
        }
        finally
        {
            var final = new CommandChunk(req.CommandId, _agentId, "stdout", string.Empty, true);
            
            // Notify UI of final output
            CommandOutputReceived?.Invoke(this, final);
            
            await SafeSendAsync(final);
        }
    }

    // File Transfer Methods
    private async Task HandleFileTransferAsync(FileTransferRequest req)
    {
        if (_connection is null) return;

        try
        {
            if (req.Type == FileTransferType.Download)
            {
                // Agent -> Server: Read file and send chunks
                await SendFileToServerAsync(req);
            }
            else if (req.Type == FileTransferType.Upload)
            {
                // Server -> Agent: Receive chunks and write file
                await ReceiveFileFromServerAsync(req);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"File transfer error: {ex.Message}");
        }
    }

    private async Task SendFileToServerAsync(FileTransferRequest req)
    {
        if (!File.Exists(req.SourcePath))
        {
            Console.WriteLine($"Source file not found: {req.SourcePath}");
            return;
        }

        var fileInfo = new System.IO.FileInfo(req.SourcePath);
        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / req.ChunkSize);
        var chunkIndex = 0;

        using var fileStream = File.OpenRead(req.SourcePath);
        var buffer = new byte[req.ChunkSize];

        while (chunkIndex < totalChunks)
        {
            var bytesRead = await fileStream.ReadAsync(buffer, 0, req.ChunkSize);
            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);

            var fileChunk = new FileTransferChunk(
                req.TransferId,
                _agentId,
                chunkIndex,
                totalChunks,
                chunk,
                chunkIndex == totalChunks - 1
            );

            await SafeSendFileChunkAsync(fileChunk);
            chunkIndex++;

            // Small delay to prevent overwhelming the connection
            await Task.Delay(10);
        }

        Console.WriteLine($"File transfer completed: {req.SourcePath} -> {req.DestinationPath}");
    }

    private async Task ReceiveFileFromServerAsync(FileTransferRequest req)
    {
        // This would be implemented when the server sends file chunks
        // For now, we'll just acknowledge the request
        Console.WriteLine($"File upload requested: {req.SourcePath} -> {req.DestinationPath}");
    }

    private async Task HandleFileOperationAsync(FileOperationRequest req)
    {
        try
        {
            bool success = false;
            var affectedPaths = new List<string>();
            string? errorMessage = null;

            switch (req.Type)
            {
                case FileOperationType.Copy:
                    success = await CopyFilesAsync(req.SourcePaths, req.DestinationPath!, req.Overwrite);
                    if (success) affectedPaths.AddRange(req.SourcePaths);
                    break;

                case FileOperationType.Move:
                    success = await MoveFilesAsync(req.SourcePaths, req.DestinationPath!, req.Overwrite);
                    if (success) affectedPaths.AddRange(req.SourcePaths);
                    break;

                case FileOperationType.Delete:
                    success = await DeleteFilesAsync(req.SourcePaths);
                    if (success) affectedPaths.AddRange(req.SourcePaths);
                    break;

                case FileOperationType.Rename:
                    if (req.SourcePaths.Count == 1 && !string.IsNullOrEmpty(req.DestinationPath))
                    {
                        success = await RenameFileAsync(req.SourcePaths[0], req.DestinationPath, req.Overwrite);
                        if (success) affectedPaths.Add(req.SourcePaths[0]);
                    }
                    break;

                case FileOperationType.CreateDirectory:
                    success = await CreateDirectoryAsync(req.SourcePaths[0]);
                    if (success) affectedPaths.Add(req.SourcePaths[0]);
                    break;
            }

            var result = new FileOperationResult(
                req.OperationId,
                _agentId,
                success,
                errorMessage,
                affectedPaths
            );

            await SafeSendFileOperationResultAsync(result);
        }
        catch (Exception ex)
        {
            var result = new FileOperationResult(
                req.OperationId,
                _agentId,
                false,
                ex.Message,
                Array.Empty<string>()
            );
            await SafeSendFileOperationResultAsync(result);
        }
    }

    private async Task<bool> CopyFilesAsync(IReadOnlyList<string> sourcePaths, string destinationPath, bool overwrite)
    {
        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                if (File.Exists(sourcePath))
                {
                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(destinationPath, fileName);
                    
                    if (File.Exists(destPath) && !overwrite)
                        continue;
                        
                    File.Copy(sourcePath, destPath, overwrite);
                }
                else if (Directory.Exists(sourcePath))
                {
                    var dirName = Path.GetDirectoryName(sourcePath);
                    var destDir = Path.Combine(destinationPath, dirName!);
                    
                    if (Directory.Exists(destDir) && !overwrite)
                        continue;
                        
                    CopyDirectory(sourcePath, destDir, overwrite);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> MoveFilesAsync(IReadOnlyList<string> sourcePaths, string destinationPath, bool overwrite)
    {
        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                if (File.Exists(sourcePath))
                {
                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(destinationPath, fileName);
                    
                    if (File.Exists(destPath) && !overwrite)
                        continue;
                        
                    File.Move(sourcePath, destPath, overwrite);
                }
                else if (Directory.Exists(sourcePath))
                {
                    var dirName = Path.GetDirectoryName(sourcePath);
                    var destDir = Path.Combine(destinationPath, dirName!);
                    
                    if (Directory.Exists(destDir) && !overwrite)
                        continue;
                        
                    Directory.Move(sourcePath, destDir);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> DeleteFilesAsync(IReadOnlyList<string> sourcePaths)
    {
        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
                else if (Directory.Exists(sourcePath))
                {
                    Directory.Delete(sourcePath, true);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RenameFileAsync(string sourcePath, string destinationPath, bool overwrite)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                if (File.Exists(destinationPath) && !overwrite)
                    return false;
                    
                File.Move(sourcePath, destinationPath, overwrite);
                return true;
            }
            else if (Directory.Exists(sourcePath))
            {
                if (Directory.Exists(destinationPath) && !overwrite)
                    return false;
                    
                Directory.Move(sourcePath, destinationPath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CreateDirectoryAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        foreach (var file in dir.GetFiles())
        {
            var tempPath = Path.Combine(destDir, file.Name);
            file.CopyTo(tempPath, overwrite);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var tempPath = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, tempPath, overwrite);
        }
    }

    private async Task SendDirectoryListingAsync(string path)
    {
        if (_connection is null) return;

        try
        {
                    var files = new List<FullVantage.Shared.FileInfo>();
        var directories = new List<FullVantage.Shared.FileInfo>();
            long totalSize = 0;
            int totalCount = 0;

            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                
                // Get directories
                foreach (var dir in dirInfo.GetDirectories())
                {
                                    directories.Add(new FullVantage.Shared.FileInfo(
                    _agentId,
                    dir.Name,
                    dir.FullName,
                    0,
                    true,
                    dir.LastWriteTime,
                    dir.Attributes.ToString(),
                    null
                ));
                    totalCount++;
                }

                // Get files
                foreach (var file in dirInfo.GetFiles())
                {
                                    files.Add(new FullVantage.Shared.FileInfo(
                    _agentId,
                    file.Name,
                    file.FullName,
                    file.Length,
                    false,
                    file.LastWriteTime,
                    file.Attributes.ToString(),
                    null
                ));
                    totalSize += file.Length;
                    totalCount++;
                }
            }

            var listing = new DirectoryListing(_agentId, path, files, directories, totalSize, totalCount);
            Console.WriteLine($"Created directory listing: {files.Count} files, {directories.Count} directories");
            
            // Send the listing back to the server
            Console.WriteLine($"Sending DirectoryListingResponse to server");
            await _connection.InvokeAsync("DirectoryListingResponse", listing);
            Console.WriteLine($"DirectoryListingResponse sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting directory listing: {ex.Message}");
        }
    }

    private async Task SendFileInfoAsync(string path)
    {
        if (_connection is null) return;

        try
        {
            FullVantage.Shared.FileInfo? fileInfo = null;

            if (File.Exists(path))
            {
                var file = new System.IO.FileInfo(path);
                fileInfo = new FullVantage.Shared.FileInfo(
                    _agentId,
                    file.Name,
                    file.FullName,
                    file.Length,
                    false,
                    file.LastWriteTime,
                    file.Attributes.ToString(),
                    null
                );
            }
            else if (Directory.Exists(path))
            {
                var dir = new System.IO.DirectoryInfo(path);
                fileInfo = new FullVantage.Shared.FileInfo(
                    _agentId,
                    dir.Name,
                    dir.FullName,
                    0,
                    true,
                    dir.LastWriteTime,
                    dir.Attributes.ToString(),
                    null
                );
            }

            if (fileInfo != null)
            {
                await _connection.InvokeAsync("FileInfoResponse", fileInfo);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file info: {ex.Message}");
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

    private Task SafeSendFileChunkAsync(FileTransferChunk chunk)
    {
        try
        {
            return _connection!.InvokeAsync("FileTransferChunk", chunk);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private Task SafeSendFileOperationResultAsync(FileOperationResult result)
    {
        try
        {
            return _connection!.InvokeAsync("FileOperationResult", result);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private static string? TryLoadConfigServerUrl()
    {
        try
        {
            // Look for agent.config.json next to the executable (legacy support)
            var exeDir = AppContext.BaseDirectory;
            var cfgPath = Path.Combine(exeDir, "agent.config.json");
            if (File.Exists(cfgPath))
            {
                var json = File.ReadAllText(cfgPath);
                var cfg = JsonSerializer.Deserialize<AgentConfig>(json);
                var url = cfg?.ServerUrl?.Trim();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }

            // Check for a simple .txt file with just the URL (simpler approach)
            var txtPath = Path.Combine(exeDir, "server.txt");
            if (File.Exists(txtPath))
            {
                var url = File.ReadAllText(txtPath).Trim();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record AgentConfig(string? ServerUrl);
}
