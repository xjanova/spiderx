using SpiderX.Crypto;
using SpiderX.Transport;

namespace SpiderX.Core;

/// <summary>
/// Represents a remote peer in the network
/// </summary>
public class Peer : IDisposable
{
    private readonly List<IConnection> _connections = [];
    private readonly object _connectionLock = new();
    private DateTime _lastSeen;
    private bool _disposed;

    /// <summary>
    /// Gets the unique identifier of this peer.
    /// </summary>
    public SpiderId Id { get; }

    /// <summary>
    /// Gets the peer's public key for encryption.
    /// </summary>
    public byte[] PublicKey { get; }

    /// <summary>
    /// Gets or sets the display name (if known).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets the connection status.
    /// </summary>
    public PeerStatus Status { get; private set; } = PeerStatus.Disconnected;

    /// <summary>
    /// Gets or sets a value indicating whether this peer is authorized to communicate.
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Gets or sets the permission level for this peer.
    /// </summary>
    public PermissionLevel Permissions { get; set; } = PermissionLevel.None;

    /// <summary>
    /// Gets the last time this peer was seen online.
    /// </summary>
    public DateTime LastSeen
    {
        get => _lastSeen;
        internal set => _lastSeen = value;
    }

    /// <summary>
    /// Gets the current latency to this peer in milliseconds.
    /// </summary>
    public int Latency { get; internal set; }

    /// <summary>
    /// Known endpoints for this peer
    /// </summary>
    public List<EndpointInfo> KnownEndpoints { get; } = [];

    /// <summary>
    /// Active connections to this peer
    /// </summary>
    public IReadOnlyList<IConnection> Connections
    {
        get
        {
            lock (_connectionLock)
            {
                return _connections.ToList();
            }
        }
    }

    /// <summary>
    /// Best connection to use (lowest latency)
    /// </summary>
    public IConnection? BestConnection
    {
        get
        {
            lock (_connectionLock)
            {
                return _connections
                    .Where(c => c.IsConnected)
                    .OrderBy(c => c.Latency)
                    .FirstOrDefault();
            }
        }
    }

    /// <summary>
    /// Whether there's at least one active connection
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _connections.Any(c => c.IsConnected);
            }
        }
    }

    /// <summary>
    /// Event raised when peer status changes
    /// </summary>
    public event EventHandler<PeerStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Creates a new peer instance
    /// </summary>
    public Peer(SpiderId id, byte[] publicKey)
    {
        Id = id;
        PublicKey = publicKey;
        _lastSeen = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a connection to this peer
    /// </summary>
    internal void AddConnection(IConnection connection)
    {
        lock (_connectionLock)
        {
            _connections.Add(connection);
            connection.RemotePeerId = Id;
            connection.Disconnected += OnConnectionDisconnected;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Removes a connection from this peer
    /// </summary>
    internal void RemoveConnection(IConnection connection)
    {
        lock (_connectionLock)
        {
            _connections.Remove(connection);
            connection.Disconnected -= OnConnectionDisconnected;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Sends data to this peer using the best available connection
    /// </summary>
    public async Task SendAsync(byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        var connection = BestConnection
            ?? throw new InvalidOperationException("No active connection to peer");

        await connection.SendAsync(data, mode);
    }

    /// <summary>
    /// Updates peer last seen time
    /// </summary>
    internal void Touch()
    {
        _lastSeen = DateTime.UtcNow;
    }

    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        if (sender is IConnection connection)
        {
            RemoveConnection(connection);
        }
    }

    private void UpdateStatus()
    {
        var newStatus = _connections.Any(c => c.IsConnected)
            ? PeerStatus.Connected
            : PeerStatus.Disconnected;

        if (newStatus != Status)
        {
            var oldStatus = Status;
            Status = newStatus;
            StatusChanged?.Invoke(this, new PeerStatusChangedEventArgs
            {
                Peer = this,
                OldStatus = oldStatus,
                NewStatus = newStatus
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_connectionLock)
        {
            foreach (var connection in _connections)
            {
                connection.Disconnected -= OnConnectionDisconnected;
                connection.Dispose();
            }
            _connections.Clear();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Peer connection status
/// </summary>
public enum PeerStatus
{
    Disconnected,
    Connecting,
    Connected,
    Blocked
}

/// <summary>
/// Permission levels for peers
/// </summary>
[Flags]
public enum PermissionLevel
{
    None = 0,
    Chat = 1,
    VoiceCall = 2,
    FileTransfer = 4,
    ScreenShare = 8,
    All = Chat | VoiceCall | FileTransfer | ScreenShare
}

/// <summary>
/// Event args for peer status changes
/// </summary>
public class PeerStatusChangedEventArgs : EventArgs
{
    public required Peer Peer { get; init; }
    public required PeerStatus OldStatus { get; init; }
    public required PeerStatus NewStatus { get; init; }
}
