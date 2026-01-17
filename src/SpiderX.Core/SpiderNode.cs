using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpiderX.Core.Messages;
using SpiderX.Crypto;
using SpiderX.Transport;

namespace SpiderX.Core;

/// <summary>
/// Main SpiderX P2P node. This is the central component that ties everything together.
/// </summary>
public class SpiderNode : IDisposable
{
    private readonly KeyPair _keyPair;
    private readonly ILogger<SpiderNode> _logger;
    private readonly List<ITransport> _transports = [];
    private readonly SpiderNodeOptions _options;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Gets the unique identifier of this node.
    /// </summary>
    public SpiderId Id => _keyPair.Id;

    /// <summary>
    /// Gets the local ID (alias for Id).
    /// </summary>
    public SpiderId LocalId => _keyPair.Id;

    /// <summary>
    /// Gets the public key for sharing with other peers.
    /// </summary>
    public byte[] PublicKey => _keyPair.PublicKey;

    /// <summary>
    /// Gets the peer manager for handling connections.
    /// </summary>
    public PeerManager Peers { get; }

    /// <summary>
    /// Gets the list of currently connected peer IDs.
    /// </summary>
    public IReadOnlyList<SpiderId> ConnectedPeers => Peers.Peers
        .Where(p => p.IsConnected)
        .Select(p => p.Id)
        .ToList();

    /// <summary>
    /// Gets a value indicating whether the node is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Event raised when the node starts
    /// </summary>
    public event EventHandler? Started;

    /// <summary>
    /// Event raised when the node stops
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// Event raised when a message is received from any peer
    /// </summary>
    public event EventHandler<(SpiderId Sender, Message Message)>? MessageReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpiderNode"/> class with a random identity.
    /// </summary>
    public SpiderNode(SpiderNodeOptions? options = null, ILogger<SpiderNode>? logger = null)
        : this(KeyPair.Generate(), options, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpiderNode"/> class with an existing key pair.
    /// </summary>
    public SpiderNode(KeyPair keyPair, SpiderNodeOptions? options = null, ILogger<SpiderNode>? logger = null)
    {
        _keyPair = keyPair;
        _options = options ?? new SpiderNodeOptions();
        _logger = logger ?? NullLogger<SpiderNode>.Instance;
        Peers = new PeerManager(_keyPair);

        // Register for events
        Peers.PeerDiscovered += OnPeerDiscovered;
        Peers.PeerConnected += OnPeerConnected;
        Peers.PeerDisconnected += OnPeerDisconnected;
        Peers.DataReceived += OnDataReceived;
    }

    /// <summary>
    /// Creates a node from a seed phrase (deterministic identity)
    /// </summary>
    public static SpiderNode FromSeedPhrase(string seedPhrase, SpiderNodeOptions? options = null)
    {
        var keyPair = KeyPair.FromSeedPhrase(seedPhrase);
        return new SpiderNode(keyPair, options);
    }

    /// <summary>
    /// Starts the node and begins listening for connections
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Node is already running");

        _logger.LogInformation("Starting SpiderX node {NodeId}", Id.Address);

        // Start UDP transport
        if (_options.EnableUdp)
        {
            var udpTransport = new UdpTransport();
            _transports.Add(udpTransport);
            Peers.RegisterTransport(udpTransport);
            await udpTransport.StartAsync(_options.UdpPort, cancellationToken);
            _logger.LogInformation("UDP transport started on port {Port}", _options.UdpPort);
        }

        // Start TCP transport
        if (_options.EnableTcp)
        {
            var tcpTransport = new TcpTransport();
            _transports.Add(tcpTransport);
            Peers.RegisterTransport(tcpTransport);
            await tcpTransport.StartAsync(_options.TcpPort, cancellationToken);
            _logger.LogInformation("TCP transport started on port {Port}", _options.TcpPort);
        }

        // Start LAN discovery
        if (_options.EnableLanDiscovery)
        {
            await Peers.StartLanDiscoveryAsync(_options.UdpPort);
            _logger.LogInformation("LAN discovery started");
        }

        // Connect to bootstrap nodes
        foreach (var bootstrap in _options.BootstrapNodes)
        {
            _ = ConnectToBootstrapAsync(bootstrap, cancellationToken);
        }

        _isRunning = true;
        Started?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("SpiderX node started: {NodeId}", Id.Address);
    }

    /// <summary>
    /// Stops the node
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("Stopping SpiderX node");

        // Stop LAN discovery
        await Peers.StopLanDiscoveryAsync();

        // Stop transports
        foreach (var transport in _transports)
        {
            await transport.StopAsync();
            transport.Dispose();
        }
        _transports.Clear();

        _isRunning = false;
        Stopped?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("SpiderX node stopped");
    }

    /// <summary>
    /// Connects to a peer by endpoint string
    /// </summary>
    public async Task<Peer?> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var endpointInfo = EndpointInfo.Parse(endpoint);
        return await Peers.ConnectAsync(endpointInfo, cancellationToken);
    }

    /// <summary>
    /// Connects to a peer by SpiderId
    /// </summary>
    public async Task<Peer?> ConnectAsync(SpiderId peerId, CancellationToken cancellationToken = default)
    {
        return await Peers.ConnectByIdAsync(peerId, cancellationToken);
    }

    /// <summary>
    /// Sends a message to a peer
    /// </summary>
    public async Task SendMessageAsync(SpiderId peerId, Message message)
    {
        await Peers.SendAsync(peerId, message);
    }

    /// <summary>
    /// Sends a chat message to a peer
    /// </summary>
    public async Task SendChatAsync(SpiderId recipient, string content, string? replyTo = null)
    {
        var message = new ChatMessage
        {
            Content = content,
            RecipientId = recipient.Address,
            ReplyTo = replyTo
        };

        await Peers.SendAsync(recipient, message);
    }

    /// <summary>
    /// Requests permission from a peer (contact request, call request, etc.)
    /// </summary>
    public async Task RequestPermissionAsync(SpiderId peerId, string permissionType, string? displayName = null)
    {
        var request = new PermissionRequestMessage
        {
            PermissionType = permissionType,
            DisplayName = displayName ?? Id.Address[..16]
        };

        await Peers.SendAsync(peerId, request);
    }

    /// <summary>
    /// Responds to a permission request
    /// </summary>
    public async Task RespondToPermissionAsync(SpiderId peerId, string requestId, bool granted, TimeSpan? duration = null)
    {
        var response = new PermissionResponseMessage
        {
            RequestId = requestId,
            Granted = granted,
            ExpiresAt = duration.HasValue
                ? DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
                : null
        };

        await Peers.SendAsync(peerId, response);

        if (granted)
        {
            Peers.AuthorizePeer(peerId);
        }
    }

    /// <summary>
    /// Gets the shareable node address
    /// </summary>
    public string GetShareableAddress()
    {
        var endpoint = _transports
            .Select(t => t.LocalEndpoint)
            .FirstOrDefault(e => e != null);

        if (endpoint == null)
            return Id.Address;

        return $"{Id.Address}@{endpoint.Address}:{endpoint.Port}";
    }

    /// <summary>
    /// Parses a shareable address and connects
    /// </summary>
    public async Task<Peer?> ConnectByShareableAddressAsync(string shareableAddress, CancellationToken cancellationToken = default)
    {
        var parts = shareableAddress.Split('@');
        var peerId = SpiderId.Parse(parts[0]);

        if (parts.Length > 1)
        {
            var endpoint = EndpointInfo.Parse(parts[1]);
            return await Peers.ConnectAsync(endpoint, cancellationToken);
        }

        return await Peers.ConnectByIdAsync(peerId, cancellationToken);
    }

    private async Task ConnectToBootstrapAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Connecting to bootstrap node: {Endpoint}", endpoint);
            await ConnectAsync(endpoint, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to bootstrap node: {Endpoint}", endpoint);
        }
    }

    private void OnPeerDiscovered(object? sender, PeerEventArgs e)
    {
        _logger.LogDebug("Discovered peer: {PeerId}", e.Peer.Id.Address);
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e)
    {
        _logger.LogInformation("Connected to peer: {PeerId}", e.Peer.Id.Address);
    }

    private void OnPeerDisconnected(object? sender, PeerEventArgs e)
    {
        _logger.LogInformation("Disconnected from peer: {PeerId}", e.Peer.Id.Address);
    }

    private void OnDataReceived(object? sender, PeerDataEventArgs e)
    {
        MessageReceived?.Invoke(this, (e.Peer.Id, e.Message));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();

        Peers.PeerDiscovered -= OnPeerDiscovered;
        Peers.PeerConnected -= OnPeerConnected;
        Peers.PeerDisconnected -= OnPeerDisconnected;
        Peers.DataReceived -= OnDataReceived;
        Peers.Dispose();

        _keyPair.Dispose();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Configuration options for SpiderNode
/// </summary>
public class SpiderNodeOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether UDP transport is enabled.
    /// </summary>
    public bool EnableUdp { get; set; } = true;

    /// <summary>
    /// Gets or sets the UDP port to listen on.
    /// </summary>
    public int UdpPort { get; set; } = 45678;

    /// <summary>
    /// Gets or sets a value indicating whether TCP transport is enabled.
    /// </summary>
    public bool EnableTcp { get; set; } = true;

    /// <summary>
    /// Gets or sets the TCP port to listen on.
    /// </summary>
    public int TcpPort { get; set; } = 45679;

    /// <summary>
    /// Gets or sets a value indicating whether LAN discovery (mDNS/broadcast) is enabled.
    /// </summary>
    public bool EnableLanDiscovery { get; set; } = true;

    /// <summary>
    /// Gets or sets the bootstrap nodes to connect to on startup.
    /// </summary>
    public List<string> BootstrapNodes { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of peer connections.
    /// </summary>
    public int MaxPeers { get; set; } = 50;

    /// <summary>
    /// Gets or sets the keepalive interval for connections.
    /// </summary>
    public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the connection timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
