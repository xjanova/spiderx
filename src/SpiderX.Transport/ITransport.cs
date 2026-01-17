using SpiderX.Crypto;

namespace SpiderX.Transport;

/// <summary>
/// Represents a network transport mechanism for P2P communication
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Gets the transport type identifier.
    /// </summary>
    TransportType Type { get; }

    /// <summary>
    /// Gets a value indicating whether the transport is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the local endpoint information.
    /// </summary>
    EndpointInfo? LocalEndpoint { get; }

    /// <summary>
    /// Starts the transport on the specified port
    /// </summary>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the transport
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Connects to a remote peer
    /// </summary>
    Task<IConnection> ConnectAsync(EndpointInfo endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a new connection is established
    /// </summary>
    event EventHandler<ConnectionEventArgs>? ConnectionReceived;

    /// <summary>
    /// Event raised when a connection is lost
    /// </summary>
    event EventHandler<ConnectionEventArgs>? ConnectionLost;
}

/// <summary>
/// Represents an active connection to a peer
/// </summary>
public interface IConnection : IDisposable
{
    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets or sets the remote peer's SpiderId (if authenticated).
    /// </summary>
    SpiderId? RemotePeerId { get; set; }

    /// <summary>
    /// Gets the remote endpoint information.
    /// </summary>
    EndpointInfo RemoteEndpoint { get; }

    /// <summary>
    /// Gets the transport type of this connection.
    /// </summary>
    TransportType TransportType { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the round-trip time in milliseconds.
    /// </summary>
    int Latency { get; }

    /// <summary>
    /// Sends data to the remote peer
    /// </summary>
    Task SendAsync(byte[] data, DeliveryMode mode = DeliveryMode.Reliable);

    /// <summary>
    /// Event raised when data is received
    /// </summary>
    event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// Event raised when the connection is closed
    /// </summary>
    event EventHandler? Disconnected;
}

/// <summary>
/// Transport type enumeration
/// </summary>
public enum TransportType
{
    Udp,
    Tcp,
    Bluetooth,
    WifiDirect,
    WebRtc
}

/// <summary>
/// Data delivery mode
/// </summary>
public enum DeliveryMode
{
    /// <summary>
    /// Unreliable, unordered (fastest, may lose packets)
    /// </summary>
    Unreliable,

    /// <summary>
    /// Unreliable but sequenced (drops old packets)
    /// </summary>
    Sequenced,

    /// <summary>
    /// Reliable and ordered (guaranteed delivery in order)
    /// </summary>
    Reliable,

    /// <summary>
    /// Reliable but unordered (guaranteed delivery, any order)
    /// </summary>
    ReliableUnordered
}

/// <summary>
/// Network endpoint information
/// </summary>
public class EndpointInfo
{
    public required string Address { get; init; }
    public required int Port { get; init; }
    public TransportType TransportType { get; init; } = TransportType.Udp;

    public override string ToString() => $"{TransportType}://{Address}:{Port}";

    public static EndpointInfo Parse(string endpoint)
    {
        // Format: transport://address:port or address:port
        var transportType = TransportType.Udp;
        string addressPort = endpoint;

        if (endpoint.Contains("://"))
        {
            var parts = endpoint.Split("://");
            transportType = Enum.Parse<TransportType>(parts[0], ignoreCase: true);
            addressPort = parts[1];
        }

        var apParts = addressPort.Split(':');
        return new EndpointInfo
        {
            Address = apParts[0],
            Port = int.Parse(apParts[1]),
            TransportType = transportType
        };
    }
}

/// <summary>
/// Event args for connection events
/// </summary>
public class ConnectionEventArgs : EventArgs
{
    public required IConnection Connection { get; init; }
}

/// <summary>
/// Event args for data received events
/// </summary>
public class DataReceivedEventArgs : EventArgs
{
    public required byte[] Data { get; init; }
    public required IConnection Connection { get; init; }
}
