using System.Collections.Concurrent;
using SpiderX.Core;
using SpiderX.Core.Messages;
using SpiderX.Crypto;

namespace SpiderX.Services;

/// <summary>
/// Service for handling chat messages between peers
/// </summary>
public class ChatService : IDisposable
{
    private readonly SpiderNode _node;
    private readonly ConcurrentDictionary<SpiderId, ChatConversation> _conversations = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a new message is received
    /// </summary>
    public event EventHandler<ChatMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Event raised when a message is sent successfully
    /// </summary>
    public event EventHandler<ChatMessageEventArgs>? MessageSent;

    /// <summary>
    /// Event raised when message delivery fails
    /// </summary>
    public event EventHandler<ChatMessageFailedEventArgs>? MessageFailed;

    /// <summary>
    /// All conversations
    /// </summary>
    public IReadOnlyCollection<ChatConversation> Conversations => _conversations.Values.ToList();

    public ChatService(SpiderNode node)
    {
        _node = node;
        _node.Peers.DataReceived += OnDataReceived;
    }

    /// <summary>
    /// Sends a text message to a peer
    /// </summary>
    public async Task<ChatMessageItem> SendMessageAsync(SpiderId recipientId, string content, string? replyTo = null)
    {
        var conversation = GetOrCreateConversation(recipientId);

        var messageItem = new ChatMessageItem
        {
            Id = Guid.NewGuid().ToString("N"),
            SenderId = _node.Id,
            RecipientId = recipientId,
            Content = content,
            ReplyTo = replyTo,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Sending
        };

        conversation.AddMessage(messageItem);

        try
        {
            await _node.SendChatAsync(recipientId, content, replyTo);
            messageItem.Status = MessageStatus.Sent;
            MessageSent?.Invoke(this, new ChatMessageEventArgs { Message = messageItem });
        }
        catch (Exception ex)
        {
            messageItem.Status = MessageStatus.Failed;
            MessageFailed?.Invoke(this, new ChatMessageFailedEventArgs
            {
                Message = messageItem,
                Error = ex.Message
            });
        }

        return messageItem;
    }

    /// <summary>
    /// Gets a conversation with a peer
    /// </summary>
    public ChatConversation? GetConversation(SpiderId peerId)
    {
        _conversations.TryGetValue(peerId, out var conversation);
        return conversation;
    }

    /// <summary>
    /// Gets or creates a conversation with a peer
    /// </summary>
    public ChatConversation GetOrCreateConversation(SpiderId peerId)
    {
        return _conversations.GetOrAdd(peerId, id => new ChatConversation
        {
            PeerId = id,
            Peer = _node.Peers.GetPeer(id)
        });
    }

    /// <summary>
    /// Marks messages as read
    /// </summary>
    public void MarkAsRead(SpiderId peerId)
    {
        if (_conversations.TryGetValue(peerId, out var conversation))
        {
            conversation.MarkAllAsRead();
        }
    }

    /// <summary>
    /// Deletes a conversation
    /// </summary>
    public void DeleteConversation(SpiderId peerId)
    {
        _conversations.TryRemove(peerId, out _);
    }

    private void OnDataReceived(object? sender, PeerDataEventArgs e)
    {
        if (e.Message is ChatMessage chatMessage)
        {
            var conversation = GetOrCreateConversation(e.Peer.Id);

            var messageItem = new ChatMessageItem
            {
                Id = chatMessage.Id,
                SenderId = e.Peer.Id,
                RecipientId = _node.Id,
                Content = chatMessage.Content,
                ReplyTo = chatMessage.ReplyTo,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(chatMessage.Timestamp).DateTime,
                Status = MessageStatus.Delivered
            };

            conversation.AddMessage(messageItem);
            MessageReceived?.Invoke(this, new ChatMessageEventArgs { Message = messageItem });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _node.Peers.DataReceived -= OnDataReceived;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a chat conversation with a peer
/// </summary>
public class ChatConversation
{
    private readonly List<ChatMessageItem> _messages = [];
    private readonly object _lock = new();

    public required SpiderId PeerId { get; init; }
    public Peer? Peer { get; set; }
    public int UnreadCount { get; private set; }
    public DateTime LastMessageTime { get; private set; }

    public IReadOnlyList<ChatMessageItem> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToList();
            }
        }
    }

    public ChatMessageItem? LastMessage
    {
        get
        {
            lock (_lock)
            {
                return _messages.LastOrDefault();
            }
        }
    }

    internal void AddMessage(ChatMessageItem message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            LastMessageTime = message.Timestamp;

            if (message.SenderId != PeerId && message.Status == MessageStatus.Delivered)
            {
                UnreadCount++;
            }
        }
    }

    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            foreach (var msg in _messages.Where(m => m.Status == MessageStatus.Delivered))
            {
                msg.Status = MessageStatus.Read;
            }
            UnreadCount = 0;
        }
    }
}

/// <summary>
/// Represents a single chat message
/// </summary>
public class ChatMessageItem
{
    public required string Id { get; init; }
    public required SpiderId SenderId { get; init; }
    public required SpiderId RecipientId { get; init; }
    public required string Content { get; init; }
    public string? ReplyTo { get; init; }
    public required DateTime Timestamp { get; init; }
    public MessageStatus Status { get; set; }
}

/// <summary>
/// Message delivery status
/// </summary>
public enum MessageStatus
{
    Sending,
    Sent,
    Delivered,
    Read,
    Failed
}

/// <summary>
/// Event args for chat message events
/// </summary>
public class ChatMessageEventArgs : EventArgs
{
    public required ChatMessageItem Message { get; init; }
}

/// <summary>
/// Event args for failed message delivery
/// </summary>
public class ChatMessageFailedEventArgs : EventArgs
{
    public required ChatMessageItem Message { get; init; }
    public required string Error { get; init; }
}
