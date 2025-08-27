# FullVantage Agent Test Commands

## Test Commands to Try

### 1. Test Command (Simple)
- Use the "Test Command" button in the web UI
- This should send: `Get-Date | Out-String`
- Expected result: Current date/time displayed in the client's "Live Output" section

### 2. PowerShell Commands
- Use the "PowerShell" button in the web UI
- Try these commands:
  - `Get-Process | Select-Object -First 5`
  - `Get-Service | Where-Object {$_.Status -eq "Running"} | Select-Object -First 3`
  - `Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, TotalPhysicalMemory`

### 3. View Outputs
- Use the "View Outputs" button in the web UI
- This should show all command outputs for the agent

## Expected Behavior

1. **WPF Client**: Should show a modern UI with:
   - Connection status indicator (green = connected, red = disconnected)
   - Command history showing received commands
   - Live output showing real-time command results
   - Machine and user information

2. **Web UI**: Should show:
   - Agent status as "Online"
   - Command execution results
   - Command history

## Troubleshooting

If commands don't work:
1. Check if the WPF client is running and connected
2. Check if the server is running
3. Verify the agent appears as "Online" in the web UI
4. Check the WPF client's "Live Output" section for any error messages

## Testing Steps

1. Start the server: `cd server/FullVantage.Server && dotnet run`
2. Start the WPF client: `cd client/FullVantage.Agent && dotnet run`
3. Open the web UI in a browser
4. Go to the Agents page
5. Try the test commands
6. Check both the web UI and WPF client for results
