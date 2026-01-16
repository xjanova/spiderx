# SpiderX Development Guide

คู่มือการพัฒนา SpiderX P2P Mesh Network

## สารบัญ

1. [การติดตั้งสภาพแวดล้อม](#การติดตั้งสภาพแวดล้อม)
2. [โครงสร้างโปรเจค](#โครงสร้างโปรเจค)
3. [การ Build และ Run](#การ-build-และ-run)
4. [การเขียน Test](#การเขียน-test)
5. [Coding Standards](#coding-standards)
6. [Git Workflow](#git-workflow)
7. [การ Debug](#การ-debug)
8. [การเพิ่มฟีเจอร์ใหม่](#การเพิ่มฟีเจอร์ใหม่)

---

## การติดตั้งสภาพแวดล้อม

### Prerequisites

| Software | Version | Download |
|----------|---------|----------|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| Visual Studio 2022 | 17.8+ | https://visualstudio.microsoft.com/ |
| VS Code | Latest | https://code.visualstudio.com/ |

### .NET Workloads

```bash
# ติดตั้ง MAUI workload
dotnet workload install maui

# ติดตั้ง Android workload (ถ้าต้องการ)
dotnet workload install android

# ติดตั้ง iOS workload (macOS only)
dotnet workload install ios
```

### IDE Extensions

**Visual Studio:**
- .NET MAUI (built-in)
- C# Dev Kit

**VS Code:**
- C# Dev Kit
- .NET MAUI
- NuGet Package Manager

### Clone และ Setup

```bash
# Clone repository
git clone https://github.com/your-username/spiderx.git
cd spiderx

# Restore dependencies
dotnet restore

# Verify build
dotnet build
```

---

## โครงสร้างโปรเจค

```
SpiderX/
├── SpiderX.sln                 # Solution file
├── CLAUDE.md                   # Claude AI instructions
├── README.md                   # Project overview
│
├── src/
│   ├── SpiderX.Crypto/         # ชั้น Cryptography
│   │   ├── SpiderId.cs         # Peer identity (wallet address)
│   │   ├── KeyPair.cs          # Ed25519 key management
│   │   └── Encryption.cs       # AES-GCM encryption
│   │
│   ├── SpiderX.Transport/      # ชั้น Network Transport
│   │   ├── ITransport.cs       # Interface หลัก
│   │   ├── IConnection.cs      # Connection abstraction
│   │   ├── UdpTransport.cs     # UDP implementation
│   │   ├── TcpTransport.cs     # TCP implementation
│   │   └── LanDiscovery.cs     # LAN peer discovery
│   │
│   ├── SpiderX.Core/           # ชั้น P2P Engine
│   │   ├── SpiderNode.cs       # Main node class
│   │   ├── Peer.cs             # Remote peer
│   │   ├── PeerManager.cs      # Connection management
│   │   ├── DHT/                # Distributed Hash Table
│   │   │   ├── KBucket.cs      # K-bucket for routing
│   │   │   ├── RoutingTable.cs # Kademlia routing
│   │   │   └── DhtNode.cs      # DHT node entry
│   │   ├── Messages/           # Protocol messages
│   │   │   └── Message.cs      # All message types
│   │   └── NAT/                # NAT traversal
│   │       └── NatPunchthrough.cs
│   │
│   ├── SpiderX.Services/       # ชั้น Application Services
│   │   ├── ChatService.cs      # Chat messaging
│   │   ├── FileTransferService.cs  # File sharing
│   │   └── VoiceService.cs     # Voice calls
│   │
│   └── SpiderX.App/            # ชั้น UI (MAUI)
│       ├── App.xaml            # Application entry
│       ├── AppShell.xaml       # Navigation shell
│       ├── MauiProgram.cs      # DI configuration
│       ├── Views/              # XAML pages
│       ├── ViewModels/         # MVVM view models
│       ├── Services/           # App services
│       ├── Converters/         # Value converters
│       └── Resources/          # Styles, colors
│
├── tests/
│   └── SpiderX.Tests/          # Unit tests
│
└── docs/                       # Documentation
    ├── DEVELOPMENT.md          # This file
    ├── ARCHITECTURE.md         # System architecture
    ├── API.md                  # API reference
    └── PROTOCOL.md             # Protocol specification
```

### Layer Dependencies

```
┌─────────────────────────────────────┐
│           SpiderX.App               │  ← UI Layer (MAUI)
├─────────────────────────────────────┤
│         SpiderX.Services            │  ← Service Layer
├─────────────────────────────────────┤
│          SpiderX.Core               │  ← P2P Engine
├──────────────────┬──────────────────┤
│ SpiderX.Transport│  SpiderX.Crypto  │  ← Infrastructure
└──────────────────┴──────────────────┘
```

**กฎ**: Layer ล่างไม่ควรอ้างอิง Layer บน

---

## การ Build และ Run

### Build Commands

```bash
# Build ทั้ง solution
dotnet build

# Build เฉพาะ project
dotnet build src/SpiderX.Core

# Build แบบ Release
dotnet build -c Release

# Clean และ Build ใหม่
dotnet clean && dotnet build
```

### Run Commands

```bash
# Run desktop app
cd src/SpiderX.App
dotnet run

# Run with specific framework
dotnet run -f net8.0-windows10.0.19041.0  # Windows
dotnet run -f net8.0-maccatalyst          # macOS

# Run Android (ต้องมี emulator/device)
dotnet build -t:Run -f net8.0-android

# Run iOS (macOS only, ต้องมี simulator)
dotnet build -t:Run -f net8.0-ios
```

### Publish Commands

```bash
# Publish Windows
dotnet publish -f net8.0-windows10.0.19041.0 -c Release

# Publish Android APK
dotnet publish -f net8.0-android -c Release

# Publish iOS
dotnet publish -f net8.0-ios -c Release
```

---

## การเขียน Test

### Test Framework

- **xUnit** - Test framework
- **FluentAssertions** - Assertion library
- **Moq** - Mocking (ถ้าต้องการ)

### Run Tests

```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test -v detailed

# Run specific test class
dotnet test --filter "FullyQualifiedName~SpiderIdTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Structure

```csharp
namespace SpiderX.Tests;

public class SpiderIdTests
{
    [Fact]
    public void FromPublicKey_ValidKey_ReturnsValidId()
    {
        // Arrange
        var keyPair = KeyPair.Generate();

        // Act
        var id = SpiderId.FromPublicKey(keyPair.PublicKey);

        // Assert
        id.Address.Should().StartWith("spx1");
        id.Address.Length.Should().BeGreaterThan(30);
    }

    [Theory]
    [InlineData("spx1invalidaddress")]
    [InlineData("btc1wrongprefix")]
    [InlineData("")]
    public void Parse_InvalidAddress_ThrowsException(string address)
    {
        // Act & Assert
        Action act = () => SpiderId.Parse(address);
        act.Should().Throw<FormatException>();
    }
}
```

### Test Naming Convention

```
MethodName_StateUnderTest_ExpectedBehavior
```

ตัวอย่าง:
- `Encrypt_ValidData_ReturnsEncryptedBytes`
- `Connect_PeerOffline_ThrowsTimeoutException`
- `Send_LargeFile_SplitsIntoChunks`

---

## Coding Standards

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Class | PascalCase | `SpiderNode` |
| Interface | IPascalCase | `ITransport` |
| Method | PascalCase | `SendAsync` |
| Property | PascalCase | `IsConnected` |
| Private field | _camelCase | `_connections` |
| Parameter | camelCase | `peerId` |
| Constant | PascalCase | `MaxPeers` |
| Event | PascalCase | `PeerConnected` |

### Code Style

```csharp
// Use file-scoped namespace
namespace SpiderX.Core;

// Use primary constructor where appropriate
public class Peer(SpiderId id, IConnection connection)
{
    public SpiderId Id { get; } = id;
    public IConnection Connection { get; } = connection;
}

// Use expression body for simple members
public bool IsConnected => _connection?.IsConnected ?? false;

// Use async/await consistently
public async Task<Peer?> ConnectAsync(EndpointInfo endpoint, CancellationToken ct = default)
{
    // ...
}

// Use pattern matching
if (message is ChatMessage chat)
{
    await HandleChatAsync(chat);
}

// Use collection expressions
List<Peer> peers = [];
```

### Documentation

```csharp
/// <summary>
/// Represents a unique peer identifier derived from public key.
/// Similar to cryptocurrency wallet addresses.
/// </summary>
/// <remarks>
/// Format: spx1 + Base58Check(RIPEMD160(SHA256(publicKey)))
/// </remarks>
public class SpiderId
{
    /// <summary>
    /// The human-readable address string.
    /// </summary>
    /// <example>spx1A1B2C3D4E5F6G7H8I9J0...</example>
    public string Address { get; }
}
```

### Error Handling

```csharp
// Use specific exceptions
public class PeerNotFoundException : Exception
{
    public SpiderId PeerId { get; }

    public PeerNotFoundException(SpiderId peerId)
        : base($"Peer not found: {peerId.Address}")
    {
        PeerId = peerId;
    }
}

// Validate early
public async Task SendAsync(SpiderId recipient, Message message)
{
    ArgumentNullException.ThrowIfNull(recipient);
    ArgumentNullException.ThrowIfNull(message);

    if (!IsConnected)
        throw new InvalidOperationException("Not connected");

    // ...
}
```

---

## Git Workflow

### Branch Naming

| Type | Format | Example |
|------|--------|---------|
| Feature | `feature/description` | `feature/video-call` |
| Bugfix | `fix/description` | `fix/nat-traversal` |
| Refactor | `refactor/description` | `refactor/transport-layer` |
| Docs | `docs/description` | `docs/api-reference` |

### Commit Message Format

```
<type>: <short description>

<optional body>

<optional footer>
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `refactor`: Code refactoring
- `test`: Adding tests
- `docs`: Documentation
- `chore`: Maintenance

**Examples:**
```
feat: Add video call support

Implement WebRTC-based video calling between peers.
- Add VideoService class
- Add video codec negotiation
- Add UI for video call

Closes #123
```

### Pull Request Process

1. สร้าง branch จาก `main`
2. เขียน code และ tests
3. Run `dotnet test` - ต้องผ่านทั้งหมด
4. Run `dotnet build` - ไม่มี warnings
5. สร้าง PR พร้อม description
6. รอ review และ merge

---

## การ Debug

### Logging

```csharp
// Add logging to SpiderNode
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<SpiderNode>();
var node = new SpiderNode(options, logger);
```

### Debug Tips

```csharp
// Check peer state
var peer = node.Peers.GetPeer(peerId);
Console.WriteLine($"Status: {peer?.Status}");
Console.WriteLine($"Connected: {peer?.IsConnected}");
Console.WriteLine($"Latency: {peer?.Latency}ms");
Console.WriteLine($"Permissions: {peer?.Permissions}");

// List all connections
foreach (var p in node.Peers.GetAllPeers())
{
    Console.WriteLine($"{p.Id.Address[..16]}... - {p.Status}");
}

// Monitor network traffic
node.Peers.DataReceived += (s, e) =>
{
    Console.WriteLine($"Received: {e.Message.GetType().Name} from {e.Peer.Id.Address[..16]}");
};
```

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| NAT traversal fails | Symmetric NAT | Use TURN server |
| Connection timeout | Firewall blocking | Open ports 45678-45679 |
| Message not received | Signature invalid | Check key synchronization |
| High latency | TCP for real-time | Use UDP transport |

---

## การเพิ่มฟีเจอร์ใหม่

### 1. เพิ่ม Message Type ใหม่

```csharp
// 1. สร้าง message class ใน Message.cs
public class VideoCallMessage : Message
{
    public override string Type => "video_call";
    public required string CallId { get; init; }
    public required byte[] VideoFrame { get; init; }
    public required int Sequence { get; init; }
}

// 2. เพิ่มใน MessageSerializer.Deserialize()
"video_call" => JsonSerializer.Deserialize<VideoCallMessage>(json),

// 3. Handle ใน PeerManager หรือ Service
```

### 2. เพิ่ม Transport ใหม่

```csharp
// 1. Implement ITransport
public class WebRtcTransport : ITransport
{
    public TransportType Type => TransportType.WebRtc;
    public bool IsActive { get; private set; }
    // ... implement interface
}

// 2. Register ใน SpiderNode.StartAsync()
if (_options.EnableWebRtc)
{
    var webRtcTransport = new WebRtcTransport();
    _transports.Add(webRtcTransport);
    Peers.RegisterTransport(webRtcTransport);
}
```

### 3. เพิ่ม Service ใหม่

```csharp
// 1. สร้าง service class
public class VideoService : IDisposable
{
    private readonly SpiderNode _node;

    public VideoService(SpiderNode node)
    {
        _node = node;
        _node.Peers.DataReceived += OnDataReceived;
    }

    // ... implement service
}

// 2. Register ใน MauiProgram.cs
builder.Services.AddSingleton<VideoService>();
```

### 4. เพิ่มหน้า UI ใหม่

```csharp
// 1. สร้าง ViewModel
public partial class VideoCallViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isInCall;

    [RelayCommand]
    private async Task StartCallAsync() { }
}

// 2. สร้าง View (XAML)
// 3. Register route ใน AppShell.xaml.cs
Routing.RegisterRoute(nameof(VideoCallPage), typeof(VideoCallPage));

// 4. Register ใน DI
builder.Services.AddTransient<VideoCallViewModel>();
builder.Services.AddTransient<VideoCallPage>();
```

---

## Resources

- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [.NET MAUI Documentation](https://docs.microsoft.com/dotnet/maui/)
- [Kademlia DHT Paper](https://pdos.csail.mit.edu/~petar/papers/maymounkov-kademlia-lncs.pdf)
- [NAT Traversal Techniques](https://tools.ietf.org/html/rfc5245)
- [Ed25519 Specification](https://ed25519.cr.yp.to/)
