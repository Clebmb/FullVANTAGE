# Debugging Steps for File Manager Hanging Issue

## Current Status
The File Manager is hanging on "Loading directory..." which means the communication between the server and agent is not working properly.

## Step-by-Step Debugging

### 1. Check Server Console
Look at the server console where you ran `dotnet run`. You should see:
- Server startup messages
- Agent connection messages when agents connect
- API request messages when you select an agent

### 2. Check Available Agents
Open your browser and go to:
```
http://localhost:5118/api/agents
```
This should return a JSON list of connected agents.

### 3. Test Directory Listing API Directly
If you have an agent ID from step 2, test this URL:
```
http://localhost:5118/api/agents/{AGENT_ID}/files/listing?path=C:\
```
Replace `{AGENT_ID}` with the actual agent ID.

### 4. Check Server Console for API Calls
When you test the API directly, you should see in the server console:
```
[API] Directory listing requested for agent {agentId} at path C:\
[API] Agent {agentId} found: {MachineName} (Status: {Status})
[API] Agent {agentId} connection status: HasConnection={hasConnection}, ConnectionCount={connectionCount}
[API] GetDirectoryListing message sent to agent {agentId}
```

### 5. Check Agent Console
Look at the agent console (where you ran the agent) for:
```
Received GetDirectoryListing request for path: C:\
Created directory listing: {files.Count} files, {directories.Count} directories
Sending DirectoryListingResponse to server
DirectoryListingResponse sent successfully
```

### 6. Check Server Console for Response
Look for:
```
Received DirectoryListingResponse from agent {agentId} for path C:\
Directory listing stored in FileBrowserService
Directory listing broadcasted to admin clients
```

## Expected Results

**If everything works:**
1. API call returns `200 OK`
2. Server console shows all debug messages
3. Agent console shows receiving and sending messages
4. Server console shows receiving the response
5. File Manager displays the directory listing

**If there are issues:**
1. **Agent not found**: API returns `404 Not Found`
2. **No active connections**: API returns `400 Bad Request`
3. **Agent not responding**: API returns `200 OK` but no response from agent
4. **SignalR issues**: Agent receives message but doesn't send response

## Common Issues and Solutions

### Issue 1: Agent Not Connected
- **Symptom**: Agent shows as "Offline" in agents list
- **Solution**: Restart the agent and ensure it connects to the correct server URL

### Issue 2: Agent Not in SignalR Group
- **Symptom**: Agent shows as "Online" but API calls fail
- **Solution**: Check if agent is properly registered in the SignalR hub

### Issue 3: Agent Not Responding
- **Symptom**: API call succeeds but no response from agent
- **Solution**: Check agent console for errors, ensure file system access

### Issue 4: FileBrowserService Not Working
- **Symptom**: Agent responds but File Manager still shows "Loading directory"
- **Solution**: Check if FileBrowserService is properly storing responses

## Next Steps
1. Follow the debugging steps above
2. Note what you see in each console
3. Let me know the specific error messages or behavior
4. I'll help fix the specific issue identified
