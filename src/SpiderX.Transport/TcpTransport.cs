using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SpiderX.Crypto;

namespace SpiderX.Transport;

/// <summary>
/// TCP transport implementation for reliable file transfers and larger data.
/// </summary>
public class TcpTransport : ITransport
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly ConcurrentDictionary<string, TcpConnection> _connections = new();

    /// <summary>
    /// Gets the transport type.
    /// </summary>
    public TransportType Type => TransportType.Tcp;

    /// <summary>
    /// Gets a value indicating whether the transport is active.
    /// </summary>
    public bool IsActive => _listener != null;

    /// <summary>
    /// Gets the local endpoint information.
    /// </summary>
    public EndpointInfo? LocalEndpoint { get; private set; }

    /// <summary>
    /// Event raised when a connection is received.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? ConnectionReceived;

    /// <summary>
    /// Event raised when a connection is lost.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? ConnectionLost;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener != null)
            throw new InvalidOperationException("Transport already started");

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        string localIp = GetLocalIpAddress();
        LocalEndpoint = new EndpointInfo
        {
            Address = localIp,
            Port = port,
            TransportType = TransportType.Tcp
        };

        _cts = new CancellationTokenSource();
        _acceptTask = AcceptLoopAsync(_cts.Token);

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

        _listener?.Stop();
        _listener = null;
        LocalEndpoint = null;

        foreach (var conn in _connections.Values)
        {
            conn.Dispose();
        }
        _connections.Clear();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async Task<IConnection> ConnectAsync(EndpointInfo endpoint, CancellationToken cancellationToken = default)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken);

        var connectionId = $"{endpoint.Address}:{endpoint.Port}";
        var connection = new TcpConnection(connectionId, endpoint, tcpClient);

        _connections[connectionId] = connection;
        connection.Disconnected += OnConnectionDisconnected;
        _ = connection.StartReceivingAsync();

        return connection;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                HandleNewConnection(tcpClient);
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

    private void HandleNewConnection(TcpClient tcpClient)
    {
        var remoteEndpoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
        var connectionId = $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

        var endpoint = new EndpointInfo
        {
            Address = remoteEndpoint.Address.ToString(),
            Port = remoteEndpoint.Port,
            TransportType = TransportType.Tcp
        };

        var connection = new TcpConnection(connectionId, endpoint, tcpClient);
        _connections[connectionId] = connection;
        connection.Disconnected += OnConnectionDisconnected;
        _ = connection.StartReceivingAsync();

        ConnectionReceived?.Invoke(this, new ConnectionEventArgs { Connection = connection });
    }

    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        if (sender is TcpConnection connection)
        {
            _connections.TryRemove(connection.ConnectionId, out _);
            ConnectionLost?.Invoke(this, new ConnectionEventArgs { Connection = connection });
        }
    }

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
/// TCP connection implementation
/// </summary>
public class TcpConnection : IConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets or sets the remote peer's SpiderId.
    /// </summary>
    public SpiderId? RemotePeerId { get; set; }

    /// <summary>
    /// Gets the remote endpoint information.
    /// </summary>
    public EndpointInfo RemoteEndpoint { get; }

    /// <summary>
    /// Gets the transport type.
    /// </summary>
    public TransportType TransportType => TransportType.Tcp;

    /// <summary>
    /// Gets a value indicating whether the connection is active.
    /// </summary>
    public bool IsConnected => _client.Connected && !_disposed;

    /// <summary>
    /// Gets the round-trip latency in milliseconds.
    /// </summary>
    public int Latency { get; private set; }

    /// <summary>
    /// Event raised when data is received.
    /// </summary>
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    public event EventHandler? Disconnected;

    internal TcpConnection(string connectionId, EndpointInfo remoteEndpoint, TcpClient client)
    {
        ConnectionId = connectionId;
        RemoteEndpoint = remoteEndpoint;
        _client = client;
        _stream = client.GetStream();
    }

    public async Task SendAsync(byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        ThrowIfDisposed();

        // TCP is always reliable, so we ignore the mode
        // Packet format: length (4 bytes) + data
        byte[] lengthBytes = BitConverter.GetBytes(data.Length);
        await _stream.WriteAsync(lengthBytes);
        await _stream.WriteAsync(data);
        await _stream.FlushAsync();
    }

    internal async Task StartReceivingAsync()
    {
        _cts = new CancellationTokenSource();

        try
        {
            byte[] lengthBuffer = new byte[4];

            while (!_cts.Token.IsCancellationRequested && _client.Connected)
            {
                // Read length
                int bytesRead = await _stream.ReadAsync(lengthBuffer, _cts.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                int dataLength = BitConverter.ToInt32(lengthBuffer);
                if (dataLength <= 0 || dataLength > 100 * 1024 * 1024)
                {
                    break;
                }

                // Read data
                byte[] data = new byte[dataLength];
                int totalRead = 0;
                while (totalRead < dataLength)
                {
                    bytesRead = await _stream.ReadAsync(data.AsMemory(totalRead, dataLength - totalRead), _cts.Token);
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }

                if (totalRead == dataLength)
                {
                    DataReceived?.Invoke(this, new DataReceivedEventArgs
                    {
                        Data = data,
                        Connection = this
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TcpConnection));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _stream.Dispose();
        _client.Dispose();

        GC.SuppressFinalize(this);
    }
}
