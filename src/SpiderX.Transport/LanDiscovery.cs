using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SpiderX.Crypto;

namespace SpiderX.Transport;

/// <summary>
/// Discovers peers on the local network using UDP broadcast and mDNS
/// </summary>
public class LanDiscovery : IDisposable
{
    private const int DiscoveryPort = 45678;
    private const string MulticastAddress = "239.255.255.250";
    private const string ServiceType = "_spiderx._udp.local";

    private UdpClient? _broadcastClient;
    private UdpClient? _multicastClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly SpiderId _localId;
    private readonly int _servicePort;

    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    public event EventHandler<PeerDiscoveredEventArgs>? PeerLost;

    public LanDiscovery(SpiderId localId, int servicePort)
    {
        _localId = localId;
        _servicePort = servicePort;
    }

    /// <summary>
    /// Starts listening for peer announcements
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Setup UDP broadcast listener
        _broadcastClient = new UdpClient();
        _broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _broadcastClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _broadcastClient.EnableBroadcast = true;

        // Setup multicast listener
        _multicastClient = new UdpClient();
        _multicastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _multicastClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _multicastClient.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));

        _listenTask = Task.WhenAll(
            ListenBroadcastAsync(_cts.Token),
            ListenMulticastAsync(_cts.Token)
        );

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the discovery service
    /// </summary>
    public async Task StopAsync()
    {
        // Announce departure
        await AnnounceAsync(isLeaving: true);

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        _broadcastClient?.Dispose();
        _broadcastClient = null;

        _multicastClient?.DropMulticastGroup(IPAddress.Parse(MulticastAddress));
        _multicastClient?.Dispose();
        _multicastClient = null;

        if (_listenTask != null)
        {
            try
            {
                await _listenTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    /// <summary>
    /// Announces this peer's presence on the network
    /// </summary>
    public async Task AnnounceAsync(bool isLeaving = false)
    {
        var announcement = new PeerAnnouncement
        {
            PeerId = _localId.Address,
            Port = _servicePort,
            IsLeaving = isLeaving,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(announcement);
        var data = Encoding.UTF8.GetBytes(json);

        using var client = new UdpClient();
        client.EnableBroadcast = true;

        // Send via broadcast
        await client.SendAsync(data, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        // Send via multicast
        await client.SendAsync(data, new IPEndPoint(IPAddress.Parse(MulticastAddress), DiscoveryPort));
    }

    /// <summary>
    /// Actively searches for peers on the network
    /// </summary>
    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        var searchRequest = new PeerSearchRequest
        {
            RequesterId = _localId.Address,
            Port = _servicePort,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(searchRequest);
        var data = Encoding.UTF8.GetBytes(json);

        using var client = new UdpClient();
        client.EnableBroadcast = true;

        // Send search request
        for (int i = 0; i < 3; i++)
        {
            await client.SendAsync(data, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort), cancellationToken);
            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task ListenBroadcastAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _broadcastClient != null)
        {
            try
            {
                var result = await _broadcastClient.ReceiveAsync(cancellationToken);
                ProcessMessage(result.Buffer, result.RemoteEndPoint);
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

    private async Task ListenMulticastAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _multicastClient != null)
        {
            try
            {
                var result = await _multicastClient.ReceiveAsync(cancellationToken);
                ProcessMessage(result.Buffer, result.RemoteEndPoint);
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

    private void ProcessMessage(byte[] data, IPEndPoint remoteEndpoint)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);

            // Try parsing as announcement
            if (json.Contains("\"IsLeaving\""))
            {
                var announcement = JsonSerializer.Deserialize<PeerAnnouncement>(json);
                if (announcement != null && announcement.PeerId != _localId.Address)
                {
                    var eventArgs = new PeerDiscoveredEventArgs
                    {
                        PeerId = SpiderId.Parse(announcement.PeerId),
                        Endpoint = new EndpointInfo
                        {
                            Address = remoteEndpoint.Address.ToString(),
                            Port = announcement.Port,
                            TransportType = TransportType.Udp
                        }
                    };

                    if (announcement.IsLeaving)
                        PeerLost?.Invoke(this, eventArgs);
                    else
                        PeerDiscovered?.Invoke(this, eventArgs);
                }
            }
            // Try parsing as search request
            else if (json.Contains("\"RequesterId\""))
            {
                var searchRequest = JsonSerializer.Deserialize<PeerSearchRequest>(json);
                if (searchRequest != null && searchRequest.RequesterId != _localId.Address)
                {
                    // Respond to search
                    _ = AnnounceAsync();
                }
            }
        }
        catch (JsonException)
        {
            // Invalid message, ignore
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Peer announcement message
/// </summary>
public class PeerAnnouncement
{
    public required string PeerId { get; set; }
    public required int Port { get; set; }
    public bool IsLeaving { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Peer search request message
/// </summary>
public class PeerSearchRequest
{
    public required string RequesterId { get; set; }
    public required int Port { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Event args for peer discovery
/// </summary>
public class PeerDiscoveredEventArgs : EventArgs
{
    public required SpiderId PeerId { get; init; }
    public required EndpointInfo Endpoint { get; init; }
}
