# FullVANTAGE File Transfer System

## Overview

The FullVANTAGE File Transfer System provides comprehensive file management capabilities between the server and remote agents, including:

- **File Upload**: Server → Agent
- **File Download**: Agent → Server  
- **File Operations**: Copy, Move, Delete, Rename, Create Directory
- **Directory Browsing**: Navigate and view remote file systems
- **File Information**: View detailed file metadata
- **Transfer Monitoring**: Real-time progress tracking and management

## Architecture

### Components

#### 1. Server-Side Services

- **FileTransferService**: Manages active file transfers and operations
- **AgentHub**: Handles SignalR communication for file operations
- **API Endpoints**: RESTful APIs for initiating transfers and operations

#### 2. Client-Side Agent

- **AgentRunner**: Enhanced with file transfer capabilities
- **File Operations**: Local file system operations (copy, move, delete, etc.)
- **Chunked Transfer**: Efficient file transfer using configurable chunk sizes

#### 3. Web UI Components

- **FileManager**: Browse remote file systems and manage files
- **FileTransfers**: Monitor active transfers and operations
- **Real-time Updates**: Live progress via SignalR

### Data Flow

```
[Server UI] ←→ [SignalR Hub] ←→ [Agent] ←→ [Local File System]
     ↑              ↑              ↑
     ↓              ↓              ↓
[FileTransferService] ←→ [File Operations] ←→ [File System API]
```

## Features

### File Transfer

- **Chunked Transfer**: Configurable chunk sizes (default: 64KB)
- **Progress Tracking**: Real-time transfer progress updates
- **Error Handling**: Comprehensive error handling and recovery
- **Overwrite Control**: Configurable file overwrite behavior

### File Operations

- **Copy**: Copy files and directories with overwrite control
- **Move**: Move files and directories
- **Delete**: Delete files and directories (recursive)
- **Rename**: Rename files and directories
- **Create Directory**: Create new directories

### Directory Browsing

- **Navigation**: Browse remote file systems
- **File Listing**: View files and directories with metadata
- **File Info**: Detailed file information (size, dates, attributes)
- **Path Navigation**: Direct path input and navigation

## API Reference

### File Transfer Endpoints

#### Upload File
```http
POST /api/agents/{agentId}/files/upload
Content-Type: application/json

{
  "sourcePath": "C:\\server\\file.txt",
  "destinationPath": "C:\\agent\\file.txt",
  "overwrite": false
}
```

#### Download File
```http
POST /api/agents/{agentId}/files/download
Content-Type: application/json

{
  "sourcePath": "C:\\agent\\file.txt",
  "destinationPath": "C:\\server\\file.txt",
  "overwrite": false
}
```

#### File Operations
```http
POST /api/agents/{agentId}/files/operations
Content-Type: application/json

{
  "operationId": "uuid",
  "agentId": "agent-uuid",
  "type": "Copy",
  "sourcePaths": ["C:\\source\\file.txt"],
  "destinationPath": "C:\\dest\\",
  "overwrite": false
}
```

#### Directory Listing
```http
GET /api/agents/{agentId}/files/listing?path=C:\\path
```

#### File Info
```http
GET /api/agents/{agentId}/files/info?path=C:\\file.txt
```

### SignalR Methods

#### Server → Agent
- `GetDirectoryListing(path)`: Request directory contents
- `GetFileInfo(path)`: Request file information
- `FileTransferRequested(transfer)`: Initiate file transfer
- `FileOperationRequested(operation)`: Execute file operation

#### Agent → Server
- `DirectoryListingResponse(listing)`: Send directory contents
- `FileInfoResponse(fileInfo)`: Send file information
- `FileTransferChunk(chunk)`: Send file transfer chunk
- `FileOperationResult(result)`: Send operation result

## Usage Examples

### 1. Upload a File to Agent

```csharp
// Server-side
var request = new FileUploadRequest(
    "C:\\server\\document.txt",
    "C:\\agent\\documents\\document.txt",
    false
);

var response = await httpClient.PostAsJsonAsync(
    $"/api/agents/{agentId}/files/upload", 
    request
);
```

### 2. Download a File from Agent

```csharp
// Server-side
var request = new FileDownloadRequest(
    "C:\\agent\\logs\\app.log",
    "C:\\server\\logs\\app.log",
    true
);

var response = await httpClient.PostAsJsonAsync(
    $"/api/agents/{agentId}/files/download", 
    request
);
```

### 3. Copy Files on Agent

```csharp
// Server-side
var operation = new FileOperationRequest(
    Guid.NewGuid().ToString("N"),
    agentId,
    FileOperationType.Copy,
    new[] { "C:\\agent\\source\\file.txt" },
    "C:\\agent\\destination\\",
    false
);

var response = await httpClient.PostAsJsonAsync(
    $"/api/agents/{agentId}/files/operations", 
    operation
);
```

### 4. Browse Remote Directory

```csharp
// Server-side
var response = await httpClient.GetAsync(
    $"/api/agents/{agentId}/files/listing?path=C:\\Users"
);

// Agent will respond via SignalR with directory contents
```

## Configuration

### Chunk Size
Default chunk size is 64KB. This can be modified in the `FileTransferService`:

```csharp
return new FileTransferRequest(
    transferId, agentId, sourcePath, destinationPath, type, overwrite, 
    128 * 1024); // 128KB chunks
```

### Transfer Timeouts
File transfers use the default SignalR timeout. For large files, consider increasing:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl)
    .WithAutomaticReconnect()
    .Build();

// Set longer timeout for large file transfers
connection.ServerTimeout = TimeSpan.FromMinutes(30);
```

## Security Considerations

### File Path Validation
- Validate all file paths to prevent directory traversal attacks
- Restrict access to sensitive system directories
- Implement path sanitization for user input

### Authentication & Authorization
- Ensure only authorized users can access file operations
- Implement agent authentication for file operations
- Log all file operations for audit purposes

### File Size Limits
- Implement maximum file size limits
- Consider implementing rate limiting for transfers
- Monitor disk usage and implement quotas

## Error Handling

### Common Error Scenarios

1. **File Not Found**: Source file doesn't exist
2. **Access Denied**: Insufficient permissions
3. **Disk Full**: Destination disk has insufficient space
4. **Network Issues**: Connection interruptions during transfer
5. **File Locked**: File is in use by another process

### Error Recovery

- Automatic retry for network-related errors
- Graceful degradation for permission issues
- User notification for critical errors
- Logging of all error conditions

## Performance Optimization

### Transfer Optimization
- Use appropriate chunk sizes for network conditions
- Implement parallel transfers for multiple files
- Compress files when beneficial
- Use async/await for non-blocking operations

### Memory Management
- Stream files instead of loading into memory
- Dispose of resources properly
- Implement garbage collection hints for large transfers

## Monitoring & Logging

### Transfer Metrics
- Transfer speed and progress
- Success/failure rates
- File size distributions
- Agent performance metrics

### Logging
- All file operations logged
- Transfer progress events
- Error conditions with stack traces
- Performance metrics

## Troubleshooting

### Common Issues

1. **Transfers Stuck**: Check network connectivity and agent status
2. **Permission Errors**: Verify agent has access to file paths
3. **Large File Failures**: Check disk space and timeout settings
4. **Slow Transfers**: Adjust chunk size and check network conditions

### Debug Information
- Enable detailed logging in FileTransferService
- Monitor SignalR connection status
- Check agent file system permissions
- Verify network connectivity and firewall settings

## Future Enhancements

### Planned Features
- **Resume Transfers**: Resume interrupted transfers
- **Batch Operations**: Execute multiple operations simultaneously
- **File Synchronization**: Keep directories in sync
- **Compression**: Automatic file compression for large transfers
- **Encryption**: End-to-end encryption for sensitive files

### Performance Improvements
- **Parallel Transfers**: Multiple concurrent transfers
- **Delta Transfers**: Only transfer changed portions
- **Caching**: Cache frequently accessed files
- **CDN Integration**: Use CDN for large file distributions

## Support

For issues or questions regarding the File Transfer System:

1. Check the application logs for error details
2. Verify agent connectivity and permissions
3. Review network configuration and firewall settings
4. Consult the troubleshooting section above

---

*This document covers the FullVANTAGE File Transfer System v1.0. For updates and additional information, refer to the project documentation.*
