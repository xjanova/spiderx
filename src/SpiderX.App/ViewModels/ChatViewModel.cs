using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpiderX.App.Services;
using SpiderX.Crypto;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

[QueryProperty(nameof(PeerId), "peerId")]
public partial class ChatViewModel : ObservableObject
{
    private readonly ISpiderXService _spiderXService;
    private SpiderId? _peerSpiderId;

    [ObservableProperty]
    private string _peerId = "";

    [ObservableProperty]
    private string _peerName = "";

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _messageText = "";

    [ObservableProperty]
    private ObservableCollection<MessageItem> _messages = [];

    [ObservableProperty]
    private bool _isLoading;

    public ChatViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
    }

    partial void OnPeerIdChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = LoadChatAsync();
        }
    }

    [RelayCommand]
    private async Task LoadChatAsync()
    {
        if (string.IsNullOrEmpty(PeerId)) return;

        IsLoading = true;

        try
        {
            _peerSpiderId = SpiderId.Parse(PeerId);

            var peer = _spiderXService.Node?.Peers.GetPeer(_peerSpiderId);
            PeerName = peer?.DisplayName ?? PeerId[..16];
            IsOnline = peer?.IsConnected ?? false;

            // Subscribe to new messages
            if (_spiderXService.Chat != null)
            {
                _spiderXService.Chat.MessageReceived += OnMessageReceived;
                _spiderXService.Chat.MessageSent += OnMessageSent;
            }

            LoadMessages();

            // Mark as read
            _spiderXService.Chat?.MarkAsRead(_peerSpiderId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText) || _peerSpiderId == null)
            return;

        var content = MessageText.Trim();
        MessageText = "";

        try
        {
            await _spiderXService.Chat!.SendMessageAsync(_peerSpiderId, content);
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task SendFileAsync()
    {
        if (_peerSpiderId == null) return;

        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                await _spiderXService.FileTransfer!.OfferFileAsync(_peerSpiderId, result.FullPath);

                // Add system message
                Messages.Add(new MessageItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = $"Sending file: {result.FileName}",
                    IsOutgoing = true,
                    Timestamp = DateTime.UtcNow,
                    Status = "Sending..."
                });
            }
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task StartCallAsync()
    {
        if (_peerSpiderId == null) return;

        try
        {
            var call = await _spiderXService.Voice!.CallAsync(_peerSpiderId);
            await Application.Current!.MainPage!.DisplayAlert(
                "Calling",
                $"Calling {PeerName}...",
                "End Call");

            await _spiderXService.Voice.EndCallAsync(call.Id);
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void LoadMessages()
    {
        if (_peerSpiderId == null) return;

        var conversation = _spiderXService.Chat?.GetConversation(_peerSpiderId);
        if (conversation == null) return;

        Messages.Clear();

        foreach (var msg in conversation.Messages)
        {
            Messages.Add(new MessageItem
            {
                Id = msg.Id,
                Content = msg.Content,
                IsOutgoing = msg.SenderId == _spiderXService.LocalId,
                Timestamp = msg.Timestamp,
                Status = GetStatusText(msg.Status)
            });
        }
    }

    private void OnMessageReceived(object? sender, ChatMessageEventArgs e)
    {
        if (e.Message.SenderId != _peerSpiderId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(new MessageItem
            {
                Id = e.Message.Id,
                Content = e.Message.Content,
                IsOutgoing = false,
                Timestamp = e.Message.Timestamp,
                Status = ""
            });

            _spiderXService.Chat?.MarkAsRead(_peerSpiderId!);
        });
    }

    private void OnMessageSent(object? sender, ChatMessageEventArgs e)
    {
        if (e.Message.RecipientId != _peerSpiderId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(new MessageItem
            {
                Id = e.Message.Id,
                Content = e.Message.Content,
                IsOutgoing = true,
                Timestamp = e.Message.Timestamp,
                Status = GetStatusText(e.Message.Status)
            });
        });
    }

    private static string GetStatusText(MessageStatus status) => status switch
    {
        MessageStatus.Sending => "Sending...",
        MessageStatus.Sent => "Sent",
        MessageStatus.Delivered => "Delivered",
        MessageStatus.Read => "Read",
        MessageStatus.Failed => "Failed",
        _ => ""
    };
}

public class MessageItem
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required bool IsOutgoing { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Status { get; init; }

    public string TimeText => Timestamp.ToString("HH:mm");
}
