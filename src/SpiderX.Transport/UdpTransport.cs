using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SpiderX.Crypto;

namespace SpiderX.Transport;

/// <summary>
/// UDP transport implementation with NAT punch-through support.
/// Uses a custom reliable UDP protocol inspired by LiteNetLib.
/// </summary>
public class UdpTransport : ITransport
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly ConcurrentDictionary<string, UdpConnection> _connections = new();
    private int _localPort;

    public TransportType Type => TransportType.Udp;
    public bool IsActive => _udpClient != null;
    public EndpointInfo? LocalEndpoint { get; private set; }

    public event EventHandler<ConnectionEventArgs>? ConnectionReceived;
    public event EventHandler<ConnectionEventArgs>? ConnectionLost;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_udpClient != null)
            throw new InvalidOperationException("Transport already started");

        _localPort = port;
        _udpClient = new UdpClient(port);
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        // Get local IP
        string localIp = GetLocalIpAddress();
        LocalEndpoint = new EndpointInfo
        {
            Address = localIp,
            Port = port,
            TransportType = TransportType.Udp
        };

        _cts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        foreach (var conn in _connections.Values)
        {
            conn.Dispose();
        }
        _connections.Clear();

        _udpClient?.Dispose();
        _udpClient = null;
        LocalEndpoint = null;

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    public async Task<IConnection> ConnectAsync(EndpointInfo endpoint, CancellationToken cancellationToken = default)
    {
        if (_udpClient == null)
            throw new InvalidOperationException("Transport not started");

        var remoteEndpoint = new IPEndPoint(IPAddress.Parse(endpoint.Address), endpoint.Port);
        var connectionId = GetConnectionId(remoteEndpoint);

        if (_connections.TryGetValue(connectionId, out var existing))
            return existing;

        var connection = new UdpConnection(connectionId, endpoint, _udpClient, remoteEndpoint);

        // Send connection request
        await connection.SendHandshakeAsync(cancellationToken);

        _connections[connectionId] = connection;
        return connection;
    }

    /// <summary>
    /// Attempts NAT punch-through to establish connection through NAT
    /// </summary>
    public async Task<IConnection?> PunchThroughAsync(EndpointInfo endpoint, EndpointInfo? rendezvousServer = null,
        CancellationToken cancellationToken = default)
    {
        if (_udpClient == null)
            throw new InvalidOperationException("Transport not started");

        // Simple hole punching: send packets to both endpoints simultaneously
        var remoteEndpoint = new IPEndPoint(IPAddress.Parse(endpoint.Address), endpoint.Port);

        // Send multiple punch packets
        byte[] punchPacket = [(byte)PacketType.Punch, .. BitConverter.GetBytes(DateTime.UtcNow.Ticks)];

        for (int i = 0; i < 10; i++)
        {
            await _udpClient.SendAsync(punchPacket, remoteEndpoint, cancellationToken);
            await Task.Delay(100, cancellationToken);
        }

        // Try to connect
        return await ConnectAsync(endpoint, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                ProcessPacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Socket closed
                break;
            }
        }
    }

    private void ProcessPacket(byte[] data, IPEndPoint remoteEndpoint)
    {
        if (data.Length == 0) return;

        var packetType = (PacketType)data[0];
        var connectionId = GetConnectionId(remoteEndpoint);

        switch (packetType)
        {
            case PacketType.Handshake:
                HandleHandshake(connectionId, data, remoteEndpoint);
                break;

            case PacketType.HandshakeAck:
                HandleHandshakeAck(connectionId, data);
                break;

            case PacketType.Data:
            case PacketType.ReliableData:
                HandleData(connectionId, data);
                break;

            case PacketType.Ack:
                HandleAck(connectionId, data);
                break;

            case PacketType.Ping:
                HandlePing(connectionId, remoteEndpoint);
                break;

            case PacketType.Pong:
                HandlePong(connectionId);
                break;

            case PacketType.Disconnect:
                HandleDisconnect(connectionId);
                break;

            case PacketType.Punch:
                // NAT punch packet - just establishes the NAT mapping
                break;
        }
    }

    private void HandleHandshake(string connectionId, byte[] data, IPEndPoint remoteEndpoint)
    {
        if (_udpClient == null) return;

        var endpoint = new EndpointInfo
        {
            Address = remoteEndpoint.Address.ToString(),
            Port = remoteEndpoint.Port,
            TransportType = TransportType.Udp
        };

        var connection = new UdpConnection(connectionId, endpoint, _udpClient, remoteEndpoint);
        _connections[connectionId] = connection;

        // Send handshake ack
        _ = connection.SendHandshakeAckAsync();

        ConnectionReceived?.Invoke(this, new ConnectionEventArgs { Connection = connection });
    }

    private void HandleHandshakeAck(string connectionId, byte[] data)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.OnHandshakeAckReceived();
        }
    }

    private void HandleData(string connectionId, byte[] data)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.OnDataReceived(data);
        }
    }

    private void HandleAck(string connectionId, byte[] data)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.OnAckReceived(data);
        }
    }

    private void HandlePing(string connectionId, IPEndPoint remoteEndpoint)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            _ = connection.SendPongAsync();
        }
    }

    private void HandlePong(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.OnPongReceived();
        }
    }

    private void HandleDisconnect(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            connection.OnRemoteDisconnect();
            ConnectionLost?.Invoke(this, new ConnectionEventArgs { Connection = connection });
        }
    }

    private static string GetConnectionId(IPEndPoint endpoint) =>
        $"{endpoint.Address}:{endpoint.Port}";

    private static string GetLocalIpAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530);
        var endPoint = socket.LocalEndPoint as IPEndPoint;
        return endPoint?.Address.ToString() ?? "127.0.0.1";
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// UDP connection implementation
/// </summary>
public class UdpConnection : IConnection
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly ConcurrentDictionary<uint, PendingPacket> _pendingAcks = new();
    private uint _sequenceNumber;
    private DateTime _lastPingTime;
    private bool _disposed;

    public string ConnectionId { get; }
    public SpiderId? RemotePeerId { get; set; }
    public EndpointInfo RemoteEndpoint { get; }
    public TransportType TransportType => TransportType.Udp;
    public bool IsConnected { get; private set; }
    public int Latency { get; private set; }

    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler? Disconnected;

    internal UdpConnection(string connectionId, EndpointInfo remoteEndpoint, UdpClient client, IPEndPoint ipEndpoint)
    {
        ConnectionId = connectionId;
        RemoteEndpoint = remoteEndpoint;
        _client = client;
        _remoteEndpoint = ipEndpoint;
    }

    public async Task SendAsync(byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        ThrowIfDisposed();

        var packetType = mode == DeliveryMode.Unreliable ? PacketType.Data : PacketType.ReliableData;
        var seq = Interlocked.Increment(ref _sequenceNumber);

        // Packet format: type (1) + sequence (4) + data
        byte[] packet = new byte[5 + data.Length];
        packet[0] = (byte)packetType;
        BitConverter.GetBytes(seq).CopyTo(packet, 1);
        data.CopyTo(packet, 5);

        await _client.SendAsync(packet, _remoteEndpoint);

        if (mode != DeliveryMode.Unreliable)
        {
            _pendingAcks[seq] = new PendingPacket(packet, DateTime.UtcNow);
            // Start retry logic (simplified)
            _ = RetryPacketAsync(seq);
        }
    }

    internal async Task SendHandshakeAsync(CancellationToken cancellationToken = default)
    {
        byte[] packet = [(byte)PacketType.Handshake, .. BitConverter.GetBytes(DateTime.UtcNow.Ticks)];
        await _client.SendAsync(packet, _remoteEndpoint, cancellationToken);
    }

    internal async Task SendHandshakeAckAsync()
    {
        byte[] packet = [(byte)PacketType.HandshakeAck];
        await _client.SendAsync(packet, _remoteEndpoint);
        IsConnected = true;
    }

    internal void OnHandshakeAckReceived()
    {
        IsConnected = true;
    }

    internal async Task SendPongAsync()
    {
        byte[] packet = [(byte)PacketType.Pong, .. BitConverter.GetBytes(DateTime.UtcNow.Ticks)];
        await _client.SendAsync(packet, _remoteEndpoint);
    }

    internal void OnPongReceived()
    {
        Latency = (int)(DateTime.UtcNow - _lastPingTime).TotalMilliseconds;
    }

    internal void OnDataReceived(byte[] packet)
    {
        if (packet.Length < 5) return;

        var seq = BitConverter.ToUInt32(packet, 1);
        var data = packet.AsSpan(5).ToArray();

        // Send ack for reliable data
        if (packet[0] == (byte)PacketType.ReliableData)
        {
            _ = SendAckAsync(seq);
        }

        DataReceived?.Invoke(this, new DataReceivedEventArgs
        {
            Data = data,
            Connection = this
        });
    }

    internal void OnAckReceived(byte[] packet)
    {
        if (packet.Length < 5) return;
        var seq = BitConverter.ToUInt32(packet, 1);
        _pendingAcks.TryRemove(seq, out _);
    }

    internal void OnRemoteDisconnect()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private async Task SendAckAsync(uint sequence)
    {
        byte[] packet = [(byte)PacketType.Ack, .. BitConverter.GetBytes(sequence)];
        await _client.SendAsync(packet, _remoteEndpoint);
    }

    private async Task RetryPacketAsync(uint sequence)
    {
        int retries = 0;
        while (retries < 5 && _pendingAcks.ContainsKey(sequence))
        {
            await Task.Delay(100 * (1 << retries)); // Exponential backoff
            if (_pendingAcks.TryGetValue(sequence, out var pending))
            {
                await _client.SendAsync(pending.Data, _remoteEndpoint);
                retries++;
            }
        }

        // If still pending after retries, consider connection lost
        if (_pendingAcks.TryRemove(sequence, out _) && retries >= 5)
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UdpConnection));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsConnected)
        {
            byte[] disconnectPacket = [(byte)PacketType.Disconnect];
            _client.Send(disconnectPacket, _remoteEndpoint);
        }

        IsConnected = false;
        GC.SuppressFinalize(this);
    }
}

internal enum PacketType : byte
{
    Handshake = 1,
    HandshakeAck = 2,
    Data = 3,
    ReliableData = 4,
    Ack = 5,
    Ping = 6,
    Pong = 7,
    Disconnect = 8,
    Punch = 9
}

internal record PendingPacket(byte[] data, DateTime sentTime);
