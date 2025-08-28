# API Testing Guide

## Test the File Manager API Endpoints

### 1. Check Available Agents
```bash
curl http://localhost:5118/api/agents
```
This should return a list of connected agents.

### 2. Test Directory Listing Request
```bash
curl "http://localhost:5118/api/agents/{AGENT_ID}/files/listing?path=C:\"
```
Replace `{AGENT_ID}` with an actual agent ID from step 1.

### 3. Check Server Console
Look at the server console output for:
- `[API] Directory listing requested for agent {agentId} at path C:\`
- `[API] Agent {agentId} found: {MachineName} (Status: {Status})`
- `[API] Agent {agentId} connection status: HasConnection={hasConnection}, ConnectionCount={connectionCount}`
- `[API] GetDirectoryListing message sent to agent {agentId}`

### 4. Check Agent Console
Look at the agent console output for:
- `Received GetDirectoryListing request for path: C:\`
- `Created directory listing: {files.Count} files, {directories.Count} directories`
- `Sending DirectoryListingResponse to server`
- `DirectoryListingResponse sent successfully`

### 5. Check Server Console for Response
Look for:
- `Received DirectoryListingResponse from agent {agentId} for path C:\`
- `Directory listing stored in FileBrowserService`
- `Directory listing broadcasted to admin clients`

## Expected Results

If everything is working:
1. API call should return `200 OK`
2. Server console should show all the debug messages
3. Agent console should show receiving and sending messages
4. Server console should show receiving the response

## Common Issues

1. **Agent not found**: Agent ID doesn't match what's in the registry
2. **No active connections**: Agent is registered but not connected to SignalR
3. **SignalR group issue**: Agent is not properly added to the SignalR group
4. **Agent not responding**: Agent receives message but doesn't send response back

## Debugging Steps

1. **Verify agent connection**: Check if agent shows as "Online" in the agents list
2. **Check SignalR connection**: Verify agent is connected to the hub
3. **Check agent ID**: Ensure the agent ID in the URL matches the actual agent
4. **Check file system access**: Ensure agent has permission to read C:\ directory
