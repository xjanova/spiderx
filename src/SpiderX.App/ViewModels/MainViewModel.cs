using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpiderX.App.Services;
using SpiderX.Core;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;

    private bool _isConnected;
    private string _localId = "Not connected";
    private int _peerCount;
    private ObservableCollection<ConversationItem> _conversations = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string LocalId
    {
        get => _localId;
        set => SetProperty(ref _localId, value);
    }

    public int PeerCount
    {
        get => _peerCount;
        set => SetProperty(ref _peerCount, value);
    }

    public ObservableCollection<ConversationItem> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }

    public ICommand InitializeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyIdCommand { get; }
    public ICommand ShareIdCommand { get; }

    public MainViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
        _spiderXService.ConnectionStatusChanged += OnConnectionStatusChanged;

        InitializeCommand = new Command(async () => await InitializeAsync());
        RefreshCommand = new Command(async () => await RefreshAsync());
        CopyIdCommand = new Command(async () => await CopyIdAsync());
        ShareIdCommand = new Command(async () => await ShareIdAsync());
    }

    private async Task InitializeAsync()
    {
        await _spiderXService.StartAsync();

        if (_spiderXService.Node != null)
        {
            LocalId = _spiderXService.LocalId?.Address ?? "Unknown";

            _spiderXService.Chat!.MessageReceived += OnMessageReceived;
            _spiderXService.Node.Peers.PeerConnected += OnPeerConnected;
            _spiderXService.Node.Peers.PeerDisconnected += OnPeerDisconnected;

            UpdatePeerCount();
            LoadConversations();
        }
    }

    private async Task RefreshAsync()
    {
        LoadConversations();
        await Task.CompletedTask;
    }

    private async Task CopyIdAsync()
    {
        if (_spiderXService.LocalId != null)
        {
            await Clipboard.Default.SetTextAsync(_spiderXService.LocalId.Address);
        }
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

    private void LoadConversations()
    {
        if (_spiderXService.Chat == null)
            return;

        Conversations.Clear();
        foreach (var conv in _spiderXService.Chat.Conversations.OrderByDescending(c => c.LastMessageTime))
        {
            Conversations.Add(new ConversationItem
            {
                PeerId = conv.PeerId.Address,
                DisplayName = conv.Peer?.DisplayName ?? conv.PeerId.Address[..16],
                LastMessage = conv.LastMessage?.Content ?? "",
                LastMessageTime = conv.LastMessageTime,
                UnreadCount = conv.UnreadCount,
                IsOnline = conv.Peer?.IsConnected ?? false
            });
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = isConnected;
        });
    }

    private void OnMessageReceived(object? sender, ChatMessageEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadConversations();
        });
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdatePeerCount);
    }

    private void OnPeerDisconnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdatePeerCount);
    }

    private void UpdatePeerCount()
    {
        PeerCount = _spiderXService.Node?.Peers.ConnectedCount ?? 0;
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class ConversationItem
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; init; }
    public required string LastMessage { get; init; }
    public required DateTime LastMessageTime { get; init; }
    public required int UnreadCount { get; init; }
    public required bool IsOnline { get; init; }

    public string TimeDisplay => GetTimeDisplay();

    private string GetTimeDisplay()
    {
        var diff = DateTime.UtcNow - LastMessageTime;
        if (diff.TotalMinutes < 1) return "Now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d";
        return LastMessageTime.ToString("MMM d");
    }
}
