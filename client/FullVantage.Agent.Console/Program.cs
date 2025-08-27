using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FullVantage.Agent.Console;

class Program
{
    static async Task Main(string[] args)
    {
        System.Console.WriteLine("FullVANTAGE Agent Console - Starting...");
        
        // First-run consent
        var consentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FullVantage", "consent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(consentPath)!);
        var consentGiven = false;
        if (File.Exists(consentPath))
        {
            try
            {
                var json = File.ReadAllText(consentPath);
                var doc = JsonSerializer.Deserialize<ConsentState>(json);
                consentGiven = doc?.Accepted == true;
            }
            catch { }
        }

        if (!consentGiven)
        {
            System.Console.WriteLine("This app enables remote management on this device by connecting outbound to your designated server.");
            System.Console.Write("Do you consent to enroll and allow remote command execution? (y/n): ");
            var response = System.Console.ReadLine()?.ToLower();
            
            if (response != "y" && response != "yes")
            {
                System.Console.WriteLine("Consent denied. Exiting.");
                return;
            }
            
            var state = new ConsentState { Accepted = true, AcceptedAtUtc = DateTimeOffset.UtcNow };
            File.WriteAllText(consentPath, JsonSerializer.Serialize(state));
            System.Console.WriteLine("Consent accepted. Starting agent...");
        }

        // Start the agent runner
        var runner = new AgentRunner();
        try
        {
            await runner.StartAsync();
            System.Console.WriteLine("Agent started successfully. Press any key to exit...");
            System.Console.ReadKey();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error starting agent: {ex.Message}");
            System.Console.WriteLine("Press any key to exit...");
            System.Console.ReadKey();
        }
    }
}

public class ConsentState
{
    public bool Accepted { get; set; }
    public DateTimeOffset AcceptedAtUtc { get; set; }
}
