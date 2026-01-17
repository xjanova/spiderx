using System.Collections.Concurrent;
using SpiderX.Core.DHT;
using SpiderX.Core.Messages;
using SpiderX.Crypto;
using SpiderX.Transport;

namespace SpiderX.Core;

/// <summary>
/// Manages peer connections, discovery, and authentication
/// </summary>
public class PeerManager : IDisposable
{
    private readonly ConcurrentDictionary<SpiderId, Peer> _peers = new();
    private readonly ConcurrentDictionary<SpiderId, Peer> _authorizedPeers = new();
    private readonly ConcurrentDictionary<SpiderId, Peer> _blockedPeers = new();
    private readonly RoutingTable _routingTable;
    private readonly KeyPair _localKeyPair;
    private readonly List<ITransport> _transports = [];
    private LanDiscovery? _lanDiscovery;
    private bool _disposed;

    /// <summary>
    /// Local peer identity
    /// </summary>
    public SpiderId LocalId => _localKeyPair.Id;

    /// <summary>
    /// All known peers
    /// </summary>
    public IReadOnlyCollection<Peer> Peers => _peers.Values.ToList();

    /// <summary>
    /// Authorized (contact) peers
    /// </summary>
    public IReadOnlyCollection<Peer> AuthorizedPeers => _authorizedPeers.Values.ToList();

    /// <summary>
    /// Number of connected peers
    /// </summary>
    public int ConnectedCount => _peers.Values.Count(p => p.IsConnected);

    /// <summary>
    /// Event raised when a new peer is discovered
    /// </summary>
    public event EventHandler<PeerEventArgs>? PeerDiscovered;

    /// <summary>
    /// Event raised when a peer connects
    /// </summary>
    public event EventHandler<PeerEventArgs>? PeerConnected;

    /// <summary>
    /// Event raised when a peer disconnects
    /// </summary>
    public event EventHandler<PeerEventArgs>? PeerDisconnected;

    /// <summary>
    /// Event raised when a permission request is received
    /// </summary>
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;

    /// <summary>
    /// Event raised when data is received from a peer
    /// </summary>
    public event EventHandler<PeerDataEventArgs>? DataReceived;

    public PeerManager(KeyPair localKeyPair)
    {
        _localKeyPair = localKeyPair;
        _routingTable = new RoutingTable(localKeyPair.Id);
    }

    /// <summary>
    /// Registers a transport for peer communication
    /// </summary>
    public void RegisterTransport(ITransport transport)
    {
        _transports.Add(transport);
        transport.ConnectionReceived += OnConnectionReceived;
        transport.ConnectionLost += OnConnectionLost;
    }

    /// <summary>
    /// Starts LAN discovery
    /// </summary>
    public async Task StartLanDiscoveryAsync(int servicePort)
    {
        try
        {
            _lanDiscovery = new LanDiscovery(LocalId, servicePort);
            _lanDiscovery.PeerDiscovered += OnLanPeerDiscovered;
            _lanDiscovery.PeerLost += OnLanPeerLost;

            await _lanDiscovery.StartAsync();
            await _lanDiscovery.AnnounceAsync();
            await _lanDiscovery.SearchAsync();
        }
        catch (System.Net.Sockets.SocketException)
        {
            // LAN discovery failed (port in use, firewall, or permission issue)
            // Continue without LAN discovery - peer connections can still work via direct connection
            _lanDiscovery?.Dispose();
            _lanDiscovery = null;
        }
    }

    /// <summary>
    /// Stops LAN discovery
    /// </summary>
    public async Task StopLanDiscoveryAsync()
    {
        if (_lanDiscovery != null)
        {
            await _lanDiscovery.StopAsync();
            _lanDiscovery.Dispose();
            _lanDiscovery = null;
        }
    }

    /// <summary>
    /// Connects to a peer by endpoint
    /// </summary>
    public async Task<Peer?> ConnectAsync(EndpointInfo endpoint, CancellationToken cancellationToken = default)
    {
        var transport = _transports.FirstOrDefault(t => t.Type == endpoint.TransportType)
            ?? throw new InvalidOperationException($"No transport registered for {endpoint.TransportType}");

        var connection = await transport.ConnectAsync(endpoint, cancellationToken);
        return await AuthenticateConnectionAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Connects to a peer by SpiderId using DHT lookup
    /// </summary>
    public async Task<Peer?> ConnectByIdAsync(SpiderId peerId, CancellationToken cancellationToken = default)
    {
        // Check if already connected
        if (_peers.TryGetValue(peerId, out var existingPeer) && existingPeer.IsConnected)
        {
            return existingPeer;
        }

        // Try known endpoints
        if (existingPeer != null)
        {
            foreach (var endpoint in existingPeer.KnownEndpoints)
            {
                try
                {
                    var peer = await ConnectAsync(endpoint, cancellationToken);
                    if (peer?.Id == peerId)
                        return peer;
                }
                catch
                {
                    // Try next endpoint
                }
            }
        }

        // DHT lookup
        var closestNodes = _routingTable.GetClosestNodes(peerId);
        foreach (var node in closestNodes)
        {
            if (node.Id == peerId)
            {
                var endpoint = new EndpointInfo
                {
                    Address = node.Address,
                    Port = node.Port,
                    TransportType = TransportType.Udp
                };
                return await ConnectAsync(endpoint, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Authorizes a peer (adds to contacts)
    /// </summary>
    public void AuthorizePeer(SpiderId peerId, PermissionLevel permissions = PermissionLevel.All)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            peer.IsAuthorized = true;
            peer.Permissions = permissions;
            _authorizedPeers[peerId] = peer;
            _blockedPeers.TryRemove(peerId, out _);
        }
    }

    /// <summary>
    /// Revokes peer authorization
    /// </summary>
    public void RevokePeer(SpiderId peerId)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            peer.IsAuthorized = false;
            peer.Permissions = PermissionLevel.None;
            _authorizedPeers.TryRemove(peerId, out _);
        }
    }

    /// <summary>
    /// Blocks a peer
    /// </summary>
    public void BlockPeer(SpiderId peerId)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            peer.IsAuthorized = false;
            peer.Permissions = PermissionLevel.None;
            peer.Dispose();
            _blockedPeers[peerId] = peer;
            _authorizedPeers.TryRemove(peerId, out _);
        }
    }

    /// <summary>
    /// Sends a message to a peer
    /// </summary>
    public async Task SendAsync(SpiderId peerId, Message message)
    {
        if (!_peers.TryGetValue(peerId, out var peer))
            throw new InvalidOperationException("Peer not found");

        if (!peer.IsConnected)
            throw new InvalidOperationException("Peer is not connected");

        message.SenderId = LocalId.Address;
        var data = message.Serialize();

        // Encrypt if peer has public key
        if (peer.PublicKey.Length > 0)
        {
            var envelope = Encryption.EncryptForPeer(data, _localKeyPair, peer.PublicKey);
            data = envelope.Serialize();
        }

        await peer.SendAsync(data);
    }

    /// <summary>
    /// Sends a message to all connected peers
    /// </summary>
    public async Task BroadcastAsync(Message message)
    {
        message.SenderId = LocalId.Address;
        var data = message.Serialize();

        var tasks = _peers.Values
            .Where(p => p.IsConnected && p.IsAuthorized)
            .Select(async peer =>
            {
                try
                {
                    var envelope = Encryption.EncryptForPeer(data, _localKeyPair, peer.PublicKey);
                    await peer.SendAsync(envelope.Serialize());
                }
                catch
                {
                    // Log error but continue
                }
            });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Gets peer by ID
    /// </summary>
    public Peer? GetPeer(SpiderId id)
    {
        _peers.TryGetValue(id, out var peer);
        return peer;
    }

    /// <summary>
    /// Finds closest peers to a target ID using DHT
    /// </summary>
    public IReadOnlyList<DhtNode> FindClosestPeers(SpiderId targetId, int count = 20)
    {
        return _routingTable.GetClosestNodes(targetId, count);
    }

    private async Task<Peer?> AuthenticateConnectionAsync(IConnection connection, CancellationToken cancellationToken)
    {
        // Send handshake
        var handshake = new HandshakeMessage
        {
            PublicKey = _localKeyPair.PublicKey,
            SenderId = LocalId.Address
        };

        await connection.SendAsync(handshake.Serialize());

        // Wait for response
        var tcs = new TaskCompletionSource<byte[]>();
        void handler(object? s, DataReceivedEventArgs e) => tcs.TrySetResult(e.Data);

        connection.DataReceived += handler;
        try
        {
            var responseData = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            connection.DataReceived -= handler;

            var response = Message.Deserialize(responseData);
            if (response is not HandshakeAckMessage ack || !ack.Accepted)
            {
                connection.Dispose();
                return null;
            }

            // Create or update peer
            var peerId = SpiderId.FromPublicKey(ack.PublicKey);

            // Check if blocked
            if (_blockedPeers.ContainsKey(peerId))
            {
                connection.Dispose();
                return null;
            }

            var peer = _peers.GetOrAdd(peerId, _ => new Peer(peerId, ack.PublicKey));
            peer.AddConnection(connection);

            // Add to routing table
            var dhtNode = new DhtNode
            {
                Id = peerId,
                Address = connection.RemoteEndpoint.Address,
                Port = connection.RemoteEndpoint.Port
            };
            _routingTable.AddNode(dhtNode);

            // Subscribe to data
            connection.DataReceived += (s, e) => OnPeerDataReceived(peer, e.Data);

            PeerConnected?.Invoke(this, new PeerEventArgs { Peer = peer });
            return peer;
        }
        catch (TimeoutException)
        {
            connection.Dispose();
            return null;
        }
    }

    private void OnConnectionReceived(object? sender, ConnectionEventArgs e)
    {
        _ = HandleIncomingConnectionAsync(e.Connection);
    }

    private async Task HandleIncomingConnectionAsync(IConnection connection)
    {
        // Wait for handshake
        var tcs = new TaskCompletionSource<byte[]>();
        void handler(object? s, DataReceivedEventArgs e) => tcs.TrySetResult(e.Data);

        connection.DataReceived += handler;
        try
        {
            var data = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            connection.DataReceived -= handler;

            var message = Message.Deserialize(data);
            if (message is not HandshakeMessage handshake)
            {
                connection.Dispose();
                return;
            }

            var peerId = SpiderId.FromPublicKey(handshake.PublicKey);

            // Check if blocked
            if (_blockedPeers.ContainsKey(peerId))
            {
                var reject = new HandshakeAckMessage
                {
                    PublicKey = _localKeyPair.PublicKey,
                    Accepted = false,
                    Reason = "Blocked"
                };
                await connection.SendAsync(reject.Serialize());
                connection.Dispose();
                return;
            }

            // Accept connection
            var ack = new HandshakeAckMessage
            {
                PublicKey = _localKeyPair.PublicKey,
                Accepted = true,
                SenderId = LocalId.Address
            };
            await connection.SendAsync(ack.Serialize());

            // Create or update peer
            var peer = _peers.GetOrAdd(peerId, _ => new Peer(peerId, handshake.PublicKey));
            peer.AddConnection(connection);

            // Add to routing table
            var dhtNode = new DhtNode
            {
                Id = peerId,
                Address = connection.RemoteEndpoint.Address,
                Port = connection.RemoteEndpoint.Port
            };
            _routingTable.AddNode(dhtNode);

            // Subscribe to data
            connection.DataReceived += (s, e) => OnPeerDataReceived(peer, e.Data);

            // Check if this is a new peer discovery
            bool isNew = !peer.IsConnected;
            if (isNew)
            {
                PeerDiscovered?.Invoke(this, new PeerEventArgs { Peer = peer });
            }

            PeerConnected?.Invoke(this, new PeerEventArgs { Peer = peer });
        }
        catch (TimeoutException)
        {
            connection.Dispose();
        }
    }

    private void OnConnectionLost(object? sender, ConnectionEventArgs e)
    {
        var peerId = e.Connection.RemotePeerId;
        if (peerId != null && _peers.TryGetValue(peerId, out var peer))
        {
            peer.RemoveConnection(e.Connection);

            if (!peer.IsConnected)
            {
                PeerDisconnected?.Invoke(this, new PeerEventArgs { Peer = peer });
            }
        }
    }

    private void OnPeerDataReceived(Peer peer, byte[] data)
    {
        peer.Touch();

        try
        {
            // Try to decrypt if it's an encrypted envelope
            byte[] decrypted;
            try
            {
                var envelope = EncryptedEnvelope.Deserialize(data);
                decrypted = Encryption.DecryptFromPeer(envelope, _localKeyPair);
            }
            catch
            {
                // Not encrypted, use raw data
                decrypted = data;
            }

            var message = Message.Deserialize(decrypted);
            if (message != null)
            {
                HandleMessage(peer, message);
            }
        }
        catch
        {
            // Invalid message, ignore
        }
    }

    private void HandleMessage(Peer peer, Message message)
    {
        switch (message)
        {
            case PingMessage ping:
                var pong = new PongMessage { PingId = ping.Id };
                _ = SendAsync(peer.Id, pong);
                break;

            case FindNodeMessage findNode:
                var targetId = SpiderId.Parse(findNode.TargetId);
                var closest = _routingTable.GetClosestNodes(targetId);
                var response = new FindNodeResponseMessage
                {
                    RequestId = findNode.Id,
                    Nodes = closest.Select(n => new NodeInfo
                    {
                        Id = n.Id.Address,
                        Address = n.Address,
                        Port = n.Port
                    }).ToList()
                };
                _ = SendAsync(peer.Id, response);
                break;

            case PermissionRequestMessage permRequest:
                PermissionRequested?.Invoke(this, new PermissionRequestEventArgs
                {
                    Peer = peer,
                    RequestId = permRequest.Id,
                    PermissionType = permRequest.PermissionType,
                    DisplayName = permRequest.DisplayName
                });
                break;

            default:
                DataReceived?.Invoke(this, new PeerDataEventArgs
                {
                    Peer = peer,
                    Message = message
                });
                break;
        }
    }

    private void OnLanPeerDiscovered(object? sender, PeerDiscoveredEventArgs e)
    {
        if (e.PeerId == LocalId) return;

        // Try to connect
        _ = ConnectAsync(e.Endpoint);
    }

    private void OnLanPeerLost(object? sender, PeerDiscoveredEventArgs e)
    {
        _routingTable.RemoveNode(e.PeerId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lanDiscovery?.Dispose();

        foreach (var transport in _transports)
        {
            transport.ConnectionReceived -= OnConnectionReceived;
            transport.ConnectionLost -= OnConnectionLost;
        }

        foreach (var peer in _peers.Values)
        {
            peer.Dispose();
        }

        _peers.Clear();
        _authorizedPeers.Clear();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event args for peer events
/// </summary>
public class PeerEventArgs : EventArgs
{
    public required Peer Peer { get; init; }
}

/// <summary>
/// Event args for permission request events
/// </summary>
public class PermissionRequestEventArgs : EventArgs
{
    public required Peer Peer { get; init; }
    public required string RequestId { get; init; }
    public required string PermissionType { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Event args for peer data events
/// </summary>
public class PeerDataEventArgs : EventArgs
{
    public required Peer Peer { get; init; }
    public required Message Message { get; init; }
}
