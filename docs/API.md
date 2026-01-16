# SpiderX API Reference

API Reference สำหรับ SpiderX P2P Mesh Network

## สารบัญ

1. [SpiderX.Crypto](#spiderxcrypto)
2. [SpiderX.Transport](#spiderxtransport)
3. [SpiderX.Core](#spiderxcore)
4. [SpiderX.Services](#spiderxservices)

---

## SpiderX.Crypto

### SpiderId

Unique identifier สำหรับแต่ละ peer คล้าย cryptocurrency wallet address

```csharp
namespace SpiderX.Crypto;

public class SpiderId : IEquatable<SpiderId>
{
    // Properties
    public string Address { get; }      // "spx1..." format
    public byte[] Hash { get; }         // 20-byte hash

    // Static Factory Methods
    public static SpiderId FromPublicKey(byte[] publicKey);
    public static SpiderId FromHash(byte[] hash);
    public static SpiderId Parse(string address);
    public static bool TryParse(string address, out SpiderId? id);

    // Instance Methods
    public byte[] DistanceTo(SpiderId other);   // XOR distance
    public int BucketIndex(SpiderId other);     // DHT bucket index
    public bool Equals(SpiderId? other);
    public override int GetHashCode();
    public override string ToString();
}
```

**ตัวอย่าง:**
```csharp
// สร้างจาก public key
var keyPair = KeyPair.Generate();
var id = SpiderId.FromPublicKey(keyPair.PublicKey);
Console.WriteLine(id.Address); // "spx1A1B2C3D4..."

// Parse จาก string
var id = SpiderId.Parse("spx1A1B2C3D4E5F6G7H8I9J0...");

// เปรียบเทียบ
if (id1 == id2) { /* same peer */ }

// คำนวณ distance (สำหรับ DHT)
byte[] distance = id1.DistanceTo(id2);
```

---

### KeyPair

Ed25519 key pair สำหรับ signing และ key agreement

```csharp
namespace SpiderX.Crypto;

public class KeyPair : IDisposable
{
    // Properties
    public byte[] PublicKey { get; }    // 32 bytes
    public SpiderId Id { get; }         // Derived from public key

    // Static Factory Methods
    public static KeyPair Generate();
    public static KeyPair FromPrivateKey(byte[] privateKey);
    public static KeyPair FromSeedPhrase(string seedPhrase);

    // Instance Methods
    public byte[] Sign(byte[] data);
    public byte[] ComputeSharedSecret(byte[] otherPublicKey);
    public byte[] ExportPrivateKey();

    // Static Verification
    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature);

    void IDisposable.Dispose();
}
```

**ตัวอย่าง:**
```csharp
// สร้าง key pair ใหม่
var keyPair = KeyPair.Generate();

// สร้างจาก seed phrase (deterministic)
var keyPair = KeyPair.FromSeedPhrase("word1 word2 word3 ... word12");

// Sign data
byte[] signature = keyPair.Sign(data);

// Verify signature
bool isValid = KeyPair.Verify(publicKey, data, signature);

// ECDH key agreement
byte[] sharedSecret = keyPair.ComputeSharedSecret(otherPublicKey);

// Export/Import
byte[] privateKey = keyPair.ExportPrivateKey();
var restored = KeyPair.FromPrivateKey(privateKey);
```

---

### Encryption

AES-256-GCM encryption และ key derivation

```csharp
namespace SpiderX.Crypto;

public static class Encryption
{
    // Symmetric Encryption
    public static byte[] Encrypt(byte[] plaintext, byte[] sharedSecret, byte[]? associatedData = null);
    public static byte[] Decrypt(byte[] encrypted, byte[] sharedSecret, byte[]? associatedData = null);

    // Key Derivation
    public static byte[] DeriveKey(string password, byte[] salt, int iterations = 100000);
    public static byte[] GenerateSalt(int length = 16);

    // HMAC
    public static byte[] ComputeHmac(byte[] key, byte[] message);
    public static bool VerifyHmac(byte[] key, byte[] message, byte[] expectedHmac);

    // High-level Peer Encryption
    public static EncryptedEnvelope EncryptForPeer(byte[] plaintext, KeyPair sender, byte[] recipientPublicKey);
    public static byte[] DecryptFromPeer(EncryptedEnvelope envelope, KeyPair recipient);
}

public class EncryptedEnvelope
{
    public SpiderId SenderId { get; }
    public byte[] SenderPublicKey { get; }
    public byte[] EncryptedData { get; }
    public byte[] Signature { get; }

    public byte[] Serialize();
    public static EncryptedEnvelope Deserialize(byte[] data);
}
```

**ตัวอย่าง:**
```csharp
// Symmetric encryption
byte[] key = Encryption.DeriveKey("password", salt);
byte[] encrypted = Encryption.Encrypt(plaintext, key);
byte[] decrypted = Encryption.Decrypt(encrypted, key);

// Peer-to-peer encryption
var envelope = Encryption.EncryptForPeer(message, senderKeyPair, recipientPublicKey);
byte[] original = Encryption.DecryptFromPeer(envelope, recipientKeyPair);
```

---

## SpiderX.Transport

### ITransport

Interface สำหรับ network transport

```csharp
namespace SpiderX.Transport;

public interface ITransport : IDisposable
{
    TransportType Type { get; }
    bool IsActive { get; }
    EndpointInfo? LocalEndpoint { get; }

    // Events
    event EventHandler<ConnectionEventArgs>? ConnectionReceived;
    event EventHandler<ConnectionEventArgs>? ConnectionLost;

    // Methods
    Task StartAsync(int port, CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<IConnection> ConnectAsync(EndpointInfo endpoint, CancellationToken cancellationToken = default);
}

public enum TransportType
{
    Udp,
    Tcp,
    Bluetooth,
    WifiDirect
}
```

---

### IConnection

Interface สำหรับ peer connection

```csharp
namespace SpiderX.Transport;

public interface IConnection : IDisposable
{
    string ConnectionId { get; }
    SpiderId? RemotePeerId { get; set; }
    EndpointInfo RemoteEndpoint { get; }
    TransportType TransportType { get; }
    bool IsConnected { get; }
    int Latency { get; }

    // Events
    event EventHandler<DataReceivedEventArgs>? DataReceived;
    event EventHandler? Disconnected;

    // Methods
    Task SendAsync(byte[] data, DeliveryMode mode = DeliveryMode.Reliable);
}

public enum DeliveryMode
{
    Unreliable,      // UDP-like, fast but may lose packets
    Reliable,        // TCP-like, guaranteed delivery
    ReliableOrdered  // Guaranteed delivery + ordering
}
```

---

### EndpointInfo

Network endpoint information

```csharp
namespace SpiderX.Transport;

public class EndpointInfo
{
    public string Address { get; init; }
    public int Port { get; init; }
    public TransportType TransportType { get; init; }

    public static EndpointInfo Parse(string endpoint);  // "192.168.1.1:45678"
    public override string ToString();
}
```

---

### LanDiscovery

LAN peer discovery using UDP broadcast

```csharp
namespace SpiderX.Transport;

public class LanDiscovery : IDisposable
{
    public SpiderId LocalId { get; }
    public int Port { get; }

    // Events
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    public event EventHandler<PeerDiscoveredEventArgs>? PeerLost;

    // Constructor
    public LanDiscovery(SpiderId localId, byte[] publicKey, int port);

    // Methods
    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync();
    public Task AnnounceAsync();

    void IDisposable.Dispose();
}
```

**ตัวอย่าง:**
```csharp
var discovery = new LanDiscovery(myId, myPublicKey, 45678);
discovery.PeerDiscovered += (s, e) => {
    Console.WriteLine($"Found: {e.PeerId} at {e.Endpoint}");
};
await discovery.StartAsync();
```

---

## SpiderX.Core

### SpiderNode

Main P2P node class

```csharp
namespace SpiderX.Core;

public class SpiderNode : IDisposable
{
    // Properties
    public SpiderId Id { get; }
    public byte[] PublicKey { get; }
    public PeerManager Peers { get; }
    public bool IsRunning { get; }

    // Events
    public event EventHandler? Started;
    public event EventHandler? Stopped;

    // Constructors
    public SpiderNode(SpiderNodeOptions? options = null, ILogger<SpiderNode>? logger = null);
    public SpiderNode(KeyPair keyPair, SpiderNodeOptions? options = null, ILogger<SpiderNode>? logger = null);
    public static SpiderNode FromSeedPhrase(string seedPhrase, SpiderNodeOptions? options = null);

    // Lifecycle
    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync();

    // Connection
    public Task<Peer?> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);
    public Task<Peer?> ConnectAsync(SpiderId peerId, CancellationToken cancellationToken = default);
    public Task<Peer?> ConnectByShareableAddressAsync(string shareableAddress, CancellationToken ct = default);

    // Messaging
    public Task SendChatAsync(SpiderId recipient, string content, string? replyTo = null);

    // Permissions
    public Task RequestPermissionAsync(SpiderId peerId, string permissionType, string? displayName = null);
    public Task RespondToPermissionAsync(SpiderId peerId, string requestId, bool granted, TimeSpan? duration = null);

    // Utility
    public string GetShareableAddress();

    void IDisposable.Dispose();
}
```

**ตัวอย่าง:**
```csharp
// สร้าง node
var node = new SpiderNode();
// หรือ
var node = SpiderNode.FromSeedPhrase("my secret words...");

// Start
await node.StartAsync();
Console.WriteLine($"ID: {node.Id.Address}");
Console.WriteLine($"Share: {node.GetShareableAddress()}");

// Connect to peer
var peer = await node.ConnectAsync("192.168.1.100:45678");
// หรือ
var peer = await node.ConnectByShareableAddressAsync("spx1...@192.168.1.100:45678");

// Send message
await node.SendChatAsync(peer.Id, "Hello!");

// Request permission
await node.RequestPermissionAsync(peer.Id, "contact", "My Name");

// Cleanup
await node.StopAsync();
node.Dispose();
```

---

### SpiderNodeOptions

Configuration options

```csharp
namespace SpiderX.Core;

public class SpiderNodeOptions
{
    public bool EnableUdp { get; set; } = true;
    public int UdpPort { get; set; } = 45678;

    public bool EnableTcp { get; set; } = true;
    public int TcpPort { get; set; } = 45679;

    public bool EnableLanDiscovery { get; set; } = true;

    public List<string> BootstrapNodes { get; set; } = [];

    public int MaxPeers { get; set; } = 50;
    public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

---

### Peer

Represents a remote peer

```csharp
namespace SpiderX.Core;

public class Peer
{
    // Properties
    public SpiderId Id { get; }
    public byte[] PublicKey { get; }
    public PeerStatus Status { get; }
    public bool IsConnected { get; }
    public bool IsAuthorized { get; }
    public PermissionLevel Permissions { get; }
    public int Latency { get; }
    public IConnection? BestConnection { get; }
    public IReadOnlyList<IConnection> Connections { get; }
    public string? DisplayName { get; set; }
    public DateTime LastSeen { get; }

    // Events
    public event EventHandler<PeerStatusChangedEventArgs>? StatusChanged;
}

public enum PeerStatus
{
    Discovered,
    Connecting,
    Connected,
    Authenticated,
    Authorized,
    Disconnected,
    Unreachable
}

[Flags]
public enum PermissionLevel
{
    None = 0,
    Contact = 1,
    FileTransfer = 2,
    VoiceCall = 4,
    Full = Contact | FileTransfer | VoiceCall
}
```

---

### PeerManager

Manages peer connections

```csharp
namespace SpiderX.Core;

public class PeerManager : IDisposable
{
    // Properties
    public SpiderId LocalId { get; }
    public int ConnectedCount { get; }

    // Events
    public event EventHandler<PeerEventArgs>? PeerDiscovered;
    public event EventHandler<PeerEventArgs>? PeerConnected;
    public event EventHandler<PeerEventArgs>? PeerDisconnected;
    public event EventHandler<PeerDataEventArgs>? DataReceived;
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;

    // Methods
    public void RegisterTransport(ITransport transport);
    public Task StartLanDiscoveryAsync(int port);
    public Task StopLanDiscoveryAsync();

    public Task<Peer?> ConnectAsync(EndpointInfo endpoint, CancellationToken ct = default);
    public Task<Peer?> ConnectByIdAsync(SpiderId peerId, CancellationToken ct = default);

    public Peer? GetPeer(SpiderId id);
    public IReadOnlyList<Peer> GetAllPeers();
    public IReadOnlyList<Peer> GetConnectedPeers();
    public IReadOnlyList<Peer> GetAuthorizedPeers();

    public Task SendAsync<T>(SpiderId recipient, T message) where T : Message;
    public Task BroadcastAsync<T>(T message) where T : Message;

    public void AuthorizePeer(SpiderId peerId, PermissionLevel level = PermissionLevel.Contact);
    public void RevokePeer(SpiderId peerId);

    void IDisposable.Dispose();
}
```

**ตัวอย่าง:**
```csharp
// Subscribe to events
node.Peers.PeerConnected += (s, e) => {
    Console.WriteLine($"Connected: {e.Peer.Id}");
};

node.Peers.DataReceived += (s, e) => {
    if (e.Message is ChatMessage chat)
    {
        Console.WriteLine($"{e.Peer.Id}: {chat.Content}");
    }
};

node.Peers.PermissionRequested += (s, e) => {
    // Auto-accept contacts
    node.Peers.AuthorizePeer(e.Peer.Id);
};

// Get peers
var allPeers = node.Peers.GetAllPeers();
var connectedPeers = node.Peers.GetConnectedPeers();

// Send message
await node.Peers.SendAsync(peerId, new ChatMessage { Content = "Hi!" });

// Broadcast
await node.Peers.BroadcastAsync(new PingMessage());
```

---

## SpiderX.Services

### ChatService

Chat messaging service

```csharp
namespace SpiderX.Services;

public class ChatService : IDisposable
{
    // Events
    public event EventHandler<ChatMessageEventArgs>? MessageReceived;
    public event EventHandler<MessageStatusEventArgs>? MessageStatusChanged;

    // Constructor
    public ChatService(SpiderNode node, ChatServiceOptions? options = null);

    // Methods
    public Task<ChatMessage> SendAsync(SpiderId recipient, string content, string? replyTo = null);
    public Task<ChatMessage> SendAsync(SpiderId recipient, byte[] attachment, string mimeType);
    public IReadOnlyList<ChatMessage> GetHistory(SpiderId peerId, int limit = 100);
    public void MarkAsRead(SpiderId peerId, string messageId);

    void IDisposable.Dispose();
}

public class ChatMessage : Message
{
    public string Id { get; }
    public string Content { get; init; }
    public string RecipientId { get; init; }
    public string? ReplyTo { get; init; }
    public byte[]? Attachment { get; init; }
    public string? MimeType { get; init; }
    public MessageStatus Status { get; set; }
    public DateTimeOffset Timestamp { get; }
}

public enum MessageStatus
{
    Pending,
    Sent,
    Delivered,
    Read,
    Failed
}
```

**ตัวอย่าง:**
```csharp
var chatService = new ChatService(node);

chatService.MessageReceived += (s, e) => {
    Console.WriteLine($"[{e.Message.Timestamp}] {e.Sender.DisplayName}: {e.Message.Content}");
};

// Send text
var msg = await chatService.SendAsync(peerId, "Hello!");

// Send with attachment
var msg = await chatService.SendAsync(peerId, imageBytes, "image/png");

// Get history
var history = chatService.GetHistory(peerId, limit: 50);
```

---

### FileTransferService

File transfer service

```csharp
namespace SpiderX.Services;

public class FileTransferService : IDisposable
{
    // Events
    public event EventHandler<FileOfferEventArgs>? FileOfferReceived;
    public event EventHandler<TransferProgressEventArgs>? TransferProgress;
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferFailedEventArgs>? TransferFailed;

    // Properties
    public IReadOnlyCollection<FileTransfer> ActiveTransfers { get; }

    // Constructor
    public FileTransferService(SpiderNode node, string downloadPath, FileTransferOptions? options = null);

    // Methods
    public Task<FileTransfer> OfferFileAsync(SpiderId recipient, string filePath);
    public Task AcceptFileAsync(string transferId, string? savePath = null);
    public Task RejectFileAsync(string transferId);
    public void CancelTransfer(string transferId);
    public FileTransfer? GetTransfer(string transferId);

    void IDisposable.Dispose();
}

public class FileTransfer
{
    public string Id { get; }
    public SpiderId PeerId { get; }
    public TransferDirection Direction { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public string FileHash { get; }
    public TransferStatus Status { get; }
    public long BytesTransferred { get; }
    public double Progress { get; }
    public double Speed { get; }
    public TimeSpan? EstimatedTimeRemaining { get; }
}

public enum TransferStatus
{
    Pending,
    Accepted,
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Failed
}
```

**ตัวอย่าง:**
```csharp
var fileService = new FileTransferService(node, "/downloads");

// รับ file offers
fileService.FileOfferReceived += async (s, e) => {
    Console.WriteLine($"File offer: {e.Transfer.FileName} ({e.Transfer.FileSize} bytes)");
    await fileService.AcceptFileAsync(e.Transfer.Id);
};

// Progress tracking
fileService.TransferProgress += (s, e) => {
    Console.WriteLine($"Progress: {e.Transfer.Progress:P0} - {e.Transfer.Speed/1024:F1} KB/s");
};

// Send file
var transfer = await fileService.OfferFileAsync(peerId, "/path/to/file.zip");
```

---

### VoiceService

Voice call service

```csharp
namespace SpiderX.Services;

public class VoiceService : IDisposable
{
    // Events
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<CallEventArgs>? CallConnected;
    public event EventHandler<CallEndedEventArgs>? CallEnded;
    public event EventHandler<VoiceDataEventArgs>? VoiceDataReceived;

    // Properties
    public IReadOnlyCollection<VoiceCall> ActiveCalls { get; }

    // Constructor
    public VoiceService(SpiderNode node, VoiceServiceOptions? options = null);

    // Methods
    public Task<VoiceCall> CallAsync(SpiderId peerId);
    public Task AcceptCallAsync(string callId);
    public Task RejectCallAsync(string callId);
    public Task EndCallAsync(string callId);
    public Task SendVoiceDataAsync(string callId, byte[] audioData);
    public void SetMuted(string callId, bool muted);

    void IDisposable.Dispose();
}

public class VoiceCall
{
    public string Id { get; }
    public SpiderId PeerId { get; }
    public CallDirection Direction { get; }
    public CallStatus Status { get; }
    public DateTime StartTime { get; }
    public DateTime? ConnectedTime { get; }
    public TimeSpan? Duration { get; }
    public bool IsMuted { get; }
}

public enum CallStatus
{
    Ringing,
    Connected,
    Ended,
    Rejected,
    Failed
}
```

**ตัวอย่าง:**
```csharp
var voiceService = new VoiceService(node);

// Handle incoming calls
voiceService.IncomingCall += async (s, e) => {
    Console.WriteLine($"Incoming call from {e.CallerName}");
    await voiceService.AcceptCallAsync(e.Call.Id);
};

// Process voice data
voiceService.VoiceDataReceived += (s, e) => {
    // Play audio through speaker
    audioPlayer.PlaySamples(e.AudioData);
};

// Make a call
var call = await voiceService.CallAsync(peerId);

// Send voice data (from microphone)
await voiceService.SendVoiceDataAsync(call.Id, microphoneSamples);

// End call
await voiceService.EndCallAsync(call.Id);
```

---

## Event Args Reference

```csharp
// Connection events
public class ConnectionEventArgs : EventArgs
{
    public IConnection Connection { get; init; }
}

public class DataReceivedEventArgs : EventArgs
{
    public byte[] Data { get; init; }
    public IConnection Connection { get; init; }
}

// Peer events
public class PeerEventArgs : EventArgs
{
    public Peer Peer { get; init; }
}

public class PeerDataEventArgs : EventArgs
{
    public Peer Peer { get; init; }
    public Message Message { get; init; }
}

public class PermissionRequestEventArgs : EventArgs
{
    public Peer Peer { get; init; }
    public string RequestId { get; init; }
    public string PermissionType { get; init; }
    public string? DisplayName { get; init; }
}

// Discovery events
public class PeerDiscoveredEventArgs : EventArgs
{
    public SpiderId PeerId { get; init; }
    public byte[] PublicKey { get; init; }
    public EndpointInfo Endpoint { get; init; }
}

// Chat events
public class ChatMessageEventArgs : EventArgs
{
    public Peer Sender { get; init; }
    public ChatMessage Message { get; init; }
}

// File transfer events
public class FileOfferEventArgs : EventArgs
{
    public Peer Sender { get; init; }
    public FileTransfer Transfer { get; init; }
}

public class TransferProgressEventArgs : EventArgs
{
    public FileTransfer Transfer { get; init; }
}

// Voice call events
public class IncomingCallEventArgs : EventArgs
{
    public VoiceCall Call { get; init; }
    public string? CallerName { get; init; }
}

public class CallEventArgs : EventArgs
{
    public VoiceCall Call { get; init; }
}

public class VoiceDataEventArgs : EventArgs
{
    public VoiceCall Call { get; init; }
    public byte[] AudioData { get; init; }
    public int Sequence { get; init; }
}
```
