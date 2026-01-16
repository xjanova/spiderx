using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpiderX.App.Services;
using SpiderX.Core;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISpiderXService _spiderXService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _localId = "Not connected";

    [ObservableProperty]
    private int _peerCount;

    [ObservableProperty]
    private ObservableCollection<ConversationItem> _conversations = [];

    public MainViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
        _spiderXService.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        LoadConversations();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        if (_spiderXService.LocalId != null)
        {
            await Clipboard.Default.SetTextAsync(_spiderXService.LocalId.Address);
        }
    }

    [RelayCommand]
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
        if (_spiderXService.Chat == null) return;

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
