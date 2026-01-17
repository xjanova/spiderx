using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SpiderX.Core;
using SpiderX.Core.Messages;
using SpiderX.Crypto;

namespace SpiderX.Services;

/// <summary>
/// Virtual LAN Service - Creates a virtual network overlay that makes
/// internet-connected peers appear as if they're on the same local network.
/// Enables LAN games and local network discovery across the internet.
/// </summary>
public class VirtualLanService : IDisposable
{
    private const int VirtualLanPort = 45680;
    private const int BroadcastRelayPort = 45681;

    private readonly SpiderNode _node;
    private readonly ConcurrentDictionary<SpiderId, VirtualLanPeer> _vlanPeers = new();
    private readonly ConcurrentDictionary<IPAddress, SpiderId> _ipToPeerMap = new();
    private readonly IPAddress _virtualSubnet = IPAddress.Parse("10.147.0.0");
    private readonly IPAddress _virtualNetmask = IPAddress.Parse("255.255.0.0");

    private UdpClient? _broadcastRelayClient;
    private CancellationTokenSource? _cts;
    private Task? _relayTask;
    private bool _isRunning;
    private bool _disposed;
    private IPAddress? _virtualIp;

    /// <summary>
    /// Whether the Virtual LAN is active
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Local virtual IP address in the VLAN
    /// </summary>
    public IPAddress? VirtualIp => _virtualIp;

    /// <summary>
    /// Virtual subnet
    /// </summary>
    public IPAddress Subnet => _virtualSubnet;

    /// <summary>
    /// Virtual netmask
    /// </summary>
    public IPAddress Netmask => _virtualNetmask;

    /// <summary>
    /// Connected VLAN peers
    /// </summary>
    public IReadOnlyCollection<VirtualLanPeer> Peers => _vlanPeers.Values.ToList();

    /// <summary>
    /// Event raised when a peer joins the VLAN
    /// </summary>
    public event EventHandler<VirtualLanPeerEventArgs>? PeerJoined;

    /// <summary>
    /// Event raised when a peer leaves the VLAN
    /// </summary>
    public event EventHandler<VirtualLanPeerEventArgs>? PeerLeft;

    /// <summary>
    /// Event raised when virtual network traffic is received
    /// </summary>
    public event EventHandler<VirtualLanTrafficEventArgs>? TrafficReceived;

    public VirtualLanService(SpiderNode node)
    {
        _node = node;
        _node.Peers.DataReceived += OnPeerDataReceived;
    }

    /// <summary>
    /// Starts the Virtual LAN service
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Generate virtual IP based on peer ID (deterministic)
        _virtualIp = GenerateVirtualIp(_node.Id);

        // Start broadcast relay for LAN game discovery
        await StartBroadcastRelayAsync(_cts.Token);

        // Announce presence to all connected peers
        await AnnounceVlanPresenceAsync();

        _isRunning = true;
    }

    /// <summary>
    /// Stops the Virtual LAN service
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning) return;

        // Announce departure
        await AnnounceVlanDepartureAsync();

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        _broadcastRelayClient?.Dispose();
        _broadcastRelayClient = null;

        if (_relayTask != null)
        {
            try
            {
                await _relayTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _vlanPeers.Clear();
        _ipToPeerMap.Clear();
        _isRunning = false;
    }

    /// <summary>
    /// Sends a packet to a specific virtual IP
    /// </summary>
    public async Task SendPacketAsync(IPAddress destinationIp, byte[] data)
    {
        if (!_isRunning) return;

        // Check if destination is in our virtual network
        if (!IsInVirtualNetwork(destinationIp))
            return;

        // Find peer by virtual IP
        if (_ipToPeerMap.TryGetValue(destinationIp, out var peerId))
        {
            var message = new VirtualLanPacketMessage
            {
                SourceIp = _virtualIp!.ToString(),
                DestinationIp = destinationIp.ToString(),
                PacketData = data,
                PacketType = VlanPacketType.Unicast
            };

            await _node.SendMessageAsync(peerId, message);
        }
    }

    /// <summary>
    /// Broadcasts a packet to all VLAN peers (for LAN game discovery)
    /// </summary>
    public async Task BroadcastPacketAsync(byte[] data, int sourcePort, int destinationPort)
    {
        if (!_isRunning) return;

        var message = new VirtualLanPacketMessage
        {
            SourceIp = _virtualIp!.ToString(),
            DestinationIp = "255.255.255.255",
            PacketData = data,
            PacketType = VlanPacketType.Broadcast,
            SourcePort = sourcePort,
            DestinationPort = destinationPort
        };

        // Send to all VLAN peers
        var tasks = _vlanPeers.Keys.Select(peerId =>
            _node.SendMessageAsync(peerId, message));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Forwards a local broadcast to all VLAN peers
    /// </summary>
    public async Task RelayLocalBroadcastAsync(byte[] data, IPEndPoint sourceEndpoint)
    {
        if (!_isRunning) return;

        var message = new VirtualLanPacketMessage
        {
            SourceIp = _virtualIp!.ToString(),
            DestinationIp = "255.255.255.255",
            PacketData = data,
            PacketType = VlanPacketType.BroadcastRelay,
            SourcePort = sourceEndpoint.Port,
            DestinationPort = sourceEndpoint.Port
        };

        var tasks = _vlanPeers.Keys.Select(peerId =>
            _node.SendMessageAsync(peerId, message));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Gets the virtual IP for a peer
    /// </summary>
    public IPAddress? GetPeerVirtualIp(SpiderId peerId)
    {
        return _vlanPeers.TryGetValue(peerId, out var peer) ? peer.VirtualIp : null;
    }

    private Task StartBroadcastRelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            _broadcastRelayClient = new UdpClient();
            _broadcastRelayClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _broadcastRelayClient.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastRelayPort));
            _broadcastRelayClient.EnableBroadcast = true;

            _relayTask = ListenForBroadcastsAsync(cancellationToken);
        }
        catch (SocketException)
        {
        }

        return Task.CompletedTask;
    }

    private async Task ListenForBroadcastsAsync(CancellationToken cancellationToken)
    {
        if (_broadcastRelayClient == null) return;

        var gamePorts = new[]
        {
            27015, 27016,
            7777, 7778,
            25565,
            6112,
            47624,
            2302, 2303,
            28960,
            3074,
            3478, 3479, 3480
        };

        // Create listeners for common game ports
        var listeners = new List<UdpClient>();
        foreach (var port in gamePorts)
        {
            try
            {
                var listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                listener.EnableBroadcast = true;
                listeners.Add(listener);

                _ = MonitorGamePortAsync(listener, port, cancellationToken);
            }
            catch
            {
                // Port in use, skip
            }
        }

        // Main broadcast listener
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _broadcastRelayClient.ReceiveAsync(cancellationToken);

                // Forward broadcast to VLAN peers
                await RelayLocalBroadcastAsync(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }

        // Cleanup
        foreach (var listener in listeners)
        {
            listener.Dispose();
        }
    }

    private async Task MonitorGamePortAsync(UdpClient listener, int port, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(cancellationToken);

                // Check if this is a broadcast
                if (IsBroadcastPacket(result.RemoteEndPoint))
                {
                    await RelayLocalBroadcastAsync(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    private bool IsBroadcastPacket(IPEndPoint endpoint)
    {
        // Check if the destination was a broadcast address
        var bytes = endpoint.Address.GetAddressBytes();
        return bytes[3] == 255 || endpoint.Address.Equals(IPAddress.Broadcast);
    }

    private async Task AnnounceVlanPresenceAsync()
    {
        var announcement = new VirtualLanAnnounceMessage
        {
            VirtualIp = _virtualIp!.ToString(),
            IsJoining = true,
            Hostname = Environment.MachineName,
            Capabilities = VlanCapabilities.BroadcastRelay | VlanCapabilities.GameDiscovery
        };

        // Send to all authorized peers
        foreach (var peer in _node.Peers.AuthorizedPeers)
        {
            try
            {
                await _node.SendMessageAsync(peer.Id, announcement);
            }
            catch
            {
                // Peer not reachable
            }
        }
    }

    private async Task AnnounceVlanDepartureAsync()
    {
        var announcement = new VirtualLanAnnounceMessage
        {
            VirtualIp = _virtualIp!.ToString(),
            IsJoining = false,
            Hostname = Environment.MachineName
        };

        foreach (var peer in _node.Peers.AuthorizedPeers)
        {
            try
            {
                await _node.SendMessageAsync(peer.Id, announcement);
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }

    private void OnPeerDataReceived(object? sender, PeerDataEventArgs e)
    {
        switch (e.Message)
        {
            case VirtualLanAnnounceMessage announce:
                HandleVlanAnnounce(e.Peer, announce);
                break;

            case VirtualLanPacketMessage packet:
                HandleVlanPacket(e.Peer, packet);
                break;
        }
    }

    private void HandleVlanAnnounce(Peer peer, VirtualLanAnnounceMessage announce)
    {
        var virtualIp = IPAddress.Parse(announce.VirtualIp);

        if (announce.IsJoining)
        {
            var vlanPeer = new VirtualLanPeer
            {
                PeerId = peer.Id,
                VirtualIp = virtualIp,
                Hostname = announce.Hostname,
                Capabilities = announce.Capabilities,
                JoinedAt = DateTime.UtcNow
            };

            _vlanPeers[peer.Id] = vlanPeer;
            _ipToPeerMap[virtualIp] = peer.Id;

            PeerJoined?.Invoke(this, new VirtualLanPeerEventArgs { Peer = vlanPeer });

            // Send our announcement back
            _ = Task.Run(async () =>
            {
                var response = new VirtualLanAnnounceMessage
                {
                    VirtualIp = _virtualIp!.ToString(),
                    IsJoining = true,
                    Hostname = Environment.MachineName,
                    Capabilities = VlanCapabilities.BroadcastRelay | VlanCapabilities.GameDiscovery
                };
                await _node.SendMessageAsync(peer.Id, response);
            });
        }
        else
        {
            if (_vlanPeers.TryRemove(peer.Id, out var vlanPeer))
            {
                _ipToPeerMap.TryRemove(virtualIp, out _);
                PeerLeft?.Invoke(this, new VirtualLanPeerEventArgs { Peer = vlanPeer });
            }
        }
    }

    private void HandleVlanPacket(Peer peer, VirtualLanPacketMessage packet)
    {
        if (!_isRunning) return;

        TrafficReceived?.Invoke(this, new VirtualLanTrafficEventArgs
        {
            SourcePeer = peer.Id,
            SourceIp = IPAddress.Parse(packet.SourceIp),
            DestinationIp = IPAddress.Parse(packet.DestinationIp),
            Data = packet.PacketData,
            PacketType = packet.PacketType,
            SourcePort = packet.SourcePort,
            DestinationPort = packet.DestinationPort
        });

        // Handle broadcast packets - inject into local network
        if (packet.PacketType == VlanPacketType.Broadcast ||
            packet.PacketType == VlanPacketType.BroadcastRelay)
        {
            _ = InjectBroadcastLocallyAsync(packet);
        }
    }

    private async Task InjectBroadcastLocallyAsync(VirtualLanPacketMessage packet)
    {
        try
        {
            using var client = new UdpClient();
            client.EnableBroadcast = true;

            var endpoint = new IPEndPoint(IPAddress.Broadcast, packet.DestinationPort);
            await client.SendAsync(packet.PacketData, endpoint);
        }
        catch
        {
            // Failed to inject broadcast
        }
    }

    private IPAddress GenerateVirtualIp(SpiderId peerId)
    {
        // Generate deterministic IP from peer ID hash
        var hash = peerId.Hash;

        // Use 10.147.x.x range
        // First byte: 10, Second byte: 147
        // Third and fourth bytes from hash
        return new IPAddress(new byte[] { 10, 147, hash[0], hash[1] == 0 ? (byte)1 : hash[1] });
    }

    private bool IsInVirtualNetwork(IPAddress ip)
    {
        var ipBytes = ip.GetAddressBytes();
        var subnetBytes = _virtualSubnet.GetAddressBytes();
        var maskBytes = _virtualNetmask.GetAddressBytes();

        for (int i = 0; i < 4; i++)
        {
            if ((ipBytes[i] & maskBytes[i]) != (subnetBytes[i] & maskBytes[i]))
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();

        _node.Peers.DataReceived -= OnPeerDataReceived;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a peer in the Virtual LAN
/// </summary>
public class VirtualLanPeer
{
    public required SpiderId PeerId { get; init; }
    public required IPAddress VirtualIp { get; init; }
    public required string Hostname { get; init; }
    public VlanCapabilities Capabilities { get; init; }
    public DateTime JoinedAt { get; init; }

    public string DisplayName => $"{Hostname} ({VirtualIp})";
}

/// <summary>
/// Event args for VLAN peer events
/// </summary>
public class VirtualLanPeerEventArgs : EventArgs
{
    public required VirtualLanPeer Peer { get; init; }
}

/// <summary>
/// Event args for VLAN traffic events
/// </summary>
public class VirtualLanTrafficEventArgs : EventArgs
{
    public required SpiderId SourcePeer { get; init; }
    public required IPAddress SourceIp { get; init; }
    public required IPAddress DestinationIp { get; init; }
    public required byte[] Data { get; init; }
    public VlanPacketType PacketType { get; init; }
    public int SourcePort { get; init; }
    public int DestinationPort { get; init; }
}
