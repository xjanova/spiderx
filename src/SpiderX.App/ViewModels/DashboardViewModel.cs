// <copyright file="DashboardViewModel.cs" company="SpiderX">
// Copyright (c) SpiderX. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpiderX.App.Services;
using SpiderX.Core;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

/// <summary>
/// ViewModel for the Dashboard page displaying network status and quick actions.
/// </summary>
public class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;

    private bool _isConnected;
    private string _localId = "Connecting...";
    private int _peerCount;
    private int _messageCount;
    private int _unreadCount;
    private int _fileCount;
    private long _totalFileSize;
    private int _averageLatency;
    private ObservableCollection<ActivityItem> _recentActivities = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardViewModel"/> class.
    /// </summary>
    /// <param name="spiderXService">The SpiderX service.</param>
    public DashboardViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;

        ScanQRCommand = new Command(async () => await ScanQRAsync());
        ShareIdCommand = new Command(async () => await ShareIdAsync());
        FindPeersCommand = new Command(async () => await FindPeersAsync());
        SendFileCommand = new Command(async () => await SendFileAsync());
        CopyIdCommand = new Command(async () => await CopyIdAsync());
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets a value indicating whether the node is connected.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDotColor));
                OnPropertyChanged(nameof(StatusTextColor));
                OnPropertyChanged(nameof(StatusBorderColor));
                OnPropertyChanged(nameof(StatusBackgroundColor));
                OnPropertyChanged(nameof(StatusGlowColor));
            }
        }
    }

    /// <summary>
    /// Gets or sets the local SpiderX ID.
    /// </summary>
    public string LocalId
    {
        get => _localId;
        set => SetProperty(ref _localId, value);
    }

    /// <summary>
    /// Gets or sets the connected peer count.
    /// </summary>
    public int PeerCount
    {
        get => _peerCount;
        set => SetProperty(ref _peerCount, value);
    }

    /// <summary>
    /// Gets or sets the total message count.
    /// </summary>
    public int MessageCount
    {
        get => _messageCount;
        set => SetProperty(ref _messageCount, value);
    }

    /// <summary>
    /// Gets or sets the unread message count.
    /// </summary>
    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (SetProperty(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(UnreadText));
            }
        }
    }

    /// <summary>
    /// Gets the unread text display.
    /// </summary>
    public string UnreadText => UnreadCount > 0 ? $"{UnreadCount} unread" : "All read";

    /// <summary>
    /// Gets or sets the shared file count.
    /// </summary>
    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (SetProperty(ref _fileCount, value))
            {
                OnPropertyChanged(nameof(FileSizeText));
            }
        }
    }

    /// <summary>
    /// Gets or sets the total file size in bytes.
    /// </summary>
    public long TotalFileSize
    {
        get => _totalFileSize;
        set
        {
            if (SetProperty(ref _totalFileSize, value))
            {
                OnPropertyChanged(nameof(FileSizeText));
            }
        }
    }

    /// <summary>
    /// Gets the file size text display.
    /// </summary>
    public string FileSizeText => FormatFileSize(TotalFileSize);

    /// <summary>
    /// Gets or sets the average latency in milliseconds.
    /// </summary>
    public int AverageLatency
    {
        get => _averageLatency;
        set
        {
            if (SetProperty(ref _averageLatency, value))
            {
                OnPropertyChanged(nameof(LatencyText));
            }
        }
    }

    /// <summary>
    /// Gets the latency text display.
    /// </summary>
    public string LatencyText => AverageLatency > 0 ? $"{AverageLatency}ms" : "--";

    /// <summary>
    /// Gets or sets the recent activities collection.
    /// </summary>
    public ObservableCollection<ActivityItem> RecentActivities
    {
        get => _recentActivities;
        set => SetProperty(ref _recentActivities, value);
    }

    /// <summary>
    /// Gets the status text based on connection state.
    /// </summary>
    public string StatusText => IsConnected ? $"Online • {PeerCount} peers" : "Connecting...";

    /// <summary>
    /// Gets the status dot color.
    /// </summary>
    public Color StatusDotColor => IsConnected ? Color.FromArgb("#22C55E") : Color.FromArgb("#F59E0B");

    /// <summary>
    /// Gets the status text color.
    /// </summary>
    public Color StatusTextColor => IsConnected ? Color.FromArgb("#22C55E") : Color.FromArgb("#F59E0B");

    /// <summary>
    /// Gets the status border color.
    /// </summary>
    public Color StatusBorderColor => IsConnected ? Color.FromArgb("#166534") : Color.FromArgb("#78350F");

    /// <summary>
    /// Gets the status background color.
    /// </summary>
    public Color StatusBackgroundColor => IsConnected ? Color.FromArgb("#14532D") : Color.FromArgb("#451A03");

    /// <summary>
    /// Gets the status glow color.
    /// </summary>
    public Color StatusGlowColor => IsConnected ? Color.FromArgb("#22C55E") : Color.FromArgb("#F59E0B");

    /// <summary>
    /// Gets the command to scan a QR code.
    /// </summary>
    public ICommand ScanQRCommand { get; }

    /// <summary>
    /// Gets the command to share the SpiderX ID.
    /// </summary>
    public ICommand ShareIdCommand { get; }

    /// <summary>
    /// Gets the command to find nearby peers.
    /// </summary>
    public ICommand FindPeersCommand { get; }

    /// <summary>
    /// Gets the command to send a file.
    /// </summary>
    public ICommand SendFileCommand { get; }

    /// <summary>
    /// Gets the command to copy the SpiderX ID.
    /// </summary>
    public ICommand CopyIdCommand { get; }

    /// <summary>
    /// Initializes the dashboard data.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await _spiderXService.StartAsync();

        if (_spiderXService.Node != null)
        {
            LocalId = _spiderXService.LocalId?.Address ?? "Unknown";
            IsConnected = true;

            _spiderXService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _spiderXService.Node.Peers.PeerConnected += OnPeerConnected;
            _spiderXService.Node.Peers.PeerDisconnected += OnPeerDisconnected;

            if (_spiderXService.Chat != null)
            {
                _spiderXService.Chat.MessageReceived += OnMessageReceived;
            }

            UpdateStats();
            LoadRecentActivities();
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = isConnected;
            OnPropertyChanged(nameof(StatusText));
        });
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateStats();
            AddActivity("👤", "Peer Connected", e.Peer.DisplayName ?? e.Peer.Id.Address[..12], "#7C3AED");
        });
    }

    private void OnPeerDisconnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateStats();
            AddActivity("👋", "Peer Disconnected", e.Peer.DisplayName ?? e.Peer.Id.Address[..12], "#64748B");
        });
    }

    private void OnMessageReceived(object? sender, ChatMessageEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateStats();
            AddActivity("💬", "New Message", $"From {e.Message.SenderId.Address[..12]}", "#EC4899");
        });
    }

    private void UpdateStats()
    {
        if (_spiderXService.Node == null)
        {
            return;
        }

        PeerCount = _spiderXService.Node.Peers.ConnectedCount;

        if (_spiderXService.Chat != null)
        {
            MessageCount = _spiderXService.Chat.Conversations.Sum(c => c.Messages.Count);
            UnreadCount = _spiderXService.Chat.Conversations.Sum(c => c.UnreadCount);
        }

        // Calculate average latency from connected peers
        var peers = _spiderXService.Node.Peers.Peers.Where(p => p.IsConnected).ToList();
        if (peers.Count > 0)
        {
            AverageLatency = (int)peers.Average(p => p.Latency);
        }
    }

    private void LoadRecentActivities()
    {
        RecentActivities.Clear();

        // Add initial connection activity
        AddActivity("🚀", "Network Started", "SpiderX node is running", "#22C55E");
    }

    private void AddActivity(string icon, string title, string subtitle, string iconBackground)
    {
        var activity = new ActivityItem
        {
            Icon = icon,
            Title = title,
            Subtitle = subtitle,
            IconBackground = Color.FromArgb(iconBackground),
            Timestamp = DateTime.UtcNow
        };

        RecentActivities.Insert(0, activity);

        // Keep only last 10 activities
        while (RecentActivities.Count > 10)
        {
            RecentActivities.RemoveAt(RecentActivities.Count - 1);
        }
    }

    private async Task ScanQRAsync()
    {
        // Navigate to QR scanner or show not implemented
        await Application.Current!.Windows[0].Page!.DisplayAlert(
            "Scan QR",
            "QR scanning will be available soon!",
            "OK");
    }

    private async Task ShareIdAsync()
    {
        if (_spiderXService.Node != null)
        {
            var shareableAddress = _spiderXService.Node.GetShareableAddress();
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = shareableAddress,
                Title = "Share SpiderX ID"
            });
        }
    }

    private async Task FindPeersAsync()
    {
        await Shell.Current.GoToAsync("//Nearby");
    }

    private async Task SendFileAsync()
    {
        await Shell.Current.GoToAsync("//Files");
    }

    private async Task CopyIdAsync()
    {
        if (_spiderXService.LocalId != null)
        {
            await Clipboard.Default.SetTextAsync(_spiderXService.LocalId.Address);
            await Application.Current!.Windows[0].Page!.DisplayAlert(
                "Copied!",
                "SpiderX ID copied to clipboard",
                "OK");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0)
        {
            return "0 B";
        }

        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Sets the property and raises PropertyChanged if the value changed.
    /// </summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>True if the value was changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a recent activity item.
/// </summary>
public class ActivityItem
{
    /// <summary>
    /// Gets or sets the activity icon (emoji).
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// Gets or sets the activity title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets or sets the activity subtitle.
    /// </summary>
    public required string Subtitle { get; init; }

    /// <summary>
    /// Gets or sets the icon background color.
    /// </summary>
    public required Color IconBackground { get; init; }

    /// <summary>
    /// Gets or sets the activity timestamp.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the time ago display string.
    /// </summary>
    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - Timestamp;
            if (diff.TotalSeconds < 60)
            {
                return "Just now";
            }

            if (diff.TotalMinutes < 60)
            {
                return $"{(int)diff.TotalMinutes}m ago";
            }

            if (diff.TotalHours < 24)
            {
                return $"{(int)diff.TotalHours}h ago";
            }

            return $"{(int)diff.TotalDays}d ago";
        }
    }
}
