# File Manager Test Guide

## Prerequisites
1. Server is running (dotnet run in server/FullVantage.Server)
2. At least one agent is connected to the server
3. Browser is open to the server URL

## Test Steps

### 1. Basic Navigation
- Navigate to `/file-manager` in your browser
- Verify that the page loads without errors
- Check that the agents list is populated (if agents are connected)

### 2. Agent Selection
- Click on a connected agent in the agents list
- Verify that the agent is selected (highlighted)
- Check that the file browser area becomes active

### 3. Directory Listing
- After selecting an agent, the system should request a directory listing for "C:\"
- Check the browser's developer console for any JavaScript errors
- Check the server console for any backend errors
- The directory listing should appear after a short delay (500ms + agent response time)

### 4. File Operations
- Try navigating to a subdirectory by clicking on folder names
- Try the "Refresh" button to reload the current directory
- Try the "Upload" button to open the upload dialog
- Try the "Download" button to open the download dialog

### 5. File Info
- Click the "Info" button on any file or directory
- Verify that the file info dialog opens
- Check that file information is displayed correctly

## Troubleshooting

### No Files Appearing
1. Check if the agent is actually connected (should show as "Online" in agents list)
2. Check server console for any errors
3. Check browser console for JavaScript errors
4. Verify that the agent is responding to SignalR messages

### SignalR Issues
1. Check if the agent is receiving the "GetDirectoryListing" message
2. Check if the agent is sending "DirectoryListingResponse" back
3. Verify that the FileBrowserService is receiving the responses

### API Endpoints
Test these endpoints directly:
- `GET /api/agents/{agentId}/files/listing?path=C:\`
- `GET /api/agents/{agentId}/files/info?path=C:\test.txt`

## Expected Behavior
- Directory listings should appear within 1-2 seconds of selection
- File info should appear within 1-2 seconds of clicking Info
- Navigation between directories should work smoothly
- All buttons and dialogs should function properly

## Common Issues
1. **Agent not responding**: Check agent connection and SignalR hub
2. **FileInfo ambiguity**: Ensure using correct FileInfo type (FullVantage.Shared.FileInfo)
3. **Timer issues**: Check that the refresh timer is working correctly
4. **StateHasChanged errors**: Ensure proper async/await usage in Blazor
