using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace FullVantage.Agent;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            var result = MessageBox.Show(
                "This app enables remote management on this device by connecting outbound to your designated server. Do you consent to enroll and allow remote command execution, file transfer, and system info collection?",
                "FullVantage Agent Consent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
            var state = new ConsentState { Accepted = true, AcceptedAtUtc = DateTimeOffset.UtcNow };
            File.WriteAllText(consentPath, JsonSerializer.Serialize(state));
        }
    }
}

public class ConsentState
{
    public bool Accepted { get; set; }
    public DateTimeOffset AcceptedAtUtc { get; set; }
}

