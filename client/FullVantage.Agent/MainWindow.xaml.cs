using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using FullVantage.Shared;

namespace FullVantage.Agent;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<CommandHistoryItem> _commandHistory = new();
    private readonly ObservableCollection<OutputItem> _outputItems = new();
    private readonly AgentRunner _agentRunner;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Set data contexts
        CommandHistoryItems.ItemsSource = _commandHistory;
        OutputItems.ItemsSource = _outputItems;
        
        // Initialize agent runner
        _agentRunner = new AgentRunner();
        _agentRunner.CommandReceived += OnCommandReceived;
        _agentRunner.CommandOutputReceived += OnCommandOutputReceived;
        _agentRunner.StatusChanged += OnStatusChanged;
        
        // Start the agent
        _ = _agentRunner.StartAsync();
        
        // Update UI with machine info
        UpdateMachineInfo();
    }
    
    private void UpdateMachineInfo()
    {
        MachineInfoText.Text = $"Machine: {Environment.MachineName}";
        UserInfoText.Text = $"User: {Environment.UserName}";
        AgentIdText.Text = _agentRunner.AgentId;
    }
    
    private void OnCommandReceived(object? sender, CommandRequest e)
    {
        Dispatcher.Invoke(() =>
        {
            var historyItem = new CommandHistoryItem
            {
                CommandId = e.CommandId,
                ScriptOrCommand = e.ScriptOrCommand,
                Timestamp = DateTime.Now
            };
            
            _commandHistory.Insert(0, historyItem);
            
            // Keep only last 50 commands
            while (_commandHistory.Count > 50)
            {
                _commandHistory.RemoveAt(_commandHistory.Count - 1);
            }
            
            // Update visibility
            NoCommandsText.Visibility = _commandHistory.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            
            // Show notification
            ShowNotification($"New command received: {e.ScriptOrCommand}");
        });
    }
    
    private void OnCommandOutputReceived(object? sender, CommandChunk e)
    {
        Dispatcher.Invoke(() =>
        {
            var outputItem = new OutputItem
            {
                Stream = e.Stream.ToUpper(),
                Data = e.Data,
                Timestamp = DateTime.Now,
                StreamColor = e.Stream == "stderr" ? Brushes.Red : Brushes.Green
            };
            
            _outputItems.Insert(0, outputItem);
            
            // Keep only last 100 outputs
            while (_outputItems.Count > 100)
            {
                _outputItems.RemoveAt(_outputItems.Count - 1);
            }
            
            // Update visibility
            NoOutputText.Visibility = _outputItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            
            // Auto-scroll to bottom
            OutputScroll.ScrollToBottom();
        });
    }
    
    private void OnStatusChanged(object? sender, AgentStatus e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e)
            {
                case AgentStatus.Online:
                    StatusIndicator.Fill = Brushes.Green;
                    StatusText.Text = "Connected";
                    break;
                case AgentStatus.Offline:
                    StatusIndicator.Fill = Brushes.Red;
                    StatusText.Text = "Disconnected";
                    break;
                default:
                    StatusIndicator.Fill = Brushes.Gray;
                    StatusText.Text = "Connecting...";
                    break;
            }
        });
    }
    
    private void ShowNotification(string message)
    {
        // Simple notification - you could enhance this with a toast or popup
        StatusText.Text = message;
        
        // Reset status after 3 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (s, e) =>
        {
            OnStatusChanged(this, _agentRunner.CurrentStatus);
            timer.Stop();
        };
        timer.Start();
    }
    
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _commandHistory.Clear();
        _outputItems.Clear();
        NoCommandsText.Visibility = Visibility.Visible;
        NoOutputText.Visibility = Visibility.Visible;
    }
    
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        UpdateMachineInfo();
        OnStatusChanged(this, _agentRunner.CurrentStatus);
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class CommandHistoryItem
{
    public string CommandId { get; set; } = "";
    public string ScriptOrCommand { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class OutputItem
{
    public string Stream { get; set; } = "";
    public string Data { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public Brush StreamColor { get; set; } = Brushes.Black;
}