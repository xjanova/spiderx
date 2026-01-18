# CLAUDE.md - SpiderX Development Guide for Claude

This file provides guidance for Claude Code (claude.ai/code) when working with the SpiderX P2P Mesh Network codebase.

## Project Overview

SpiderX is a decentralized peer-to-peer mesh network application written in C# with .NET 8.0. It enables secure communication between devices without central servers, similar to BitTorrent but for real-time messaging, file sharing, and voice calls.

## Tech Stack

- **Language**: C# 12 / .NET 8.0
- **UI Framework**: .NET MAUI (iOS, Android, Windows, macOS)
- **Architecture**: MVVM with CommunityToolkit.Mvvm
- **Cryptography**: System.Security.Cryptography (Ed25519, AES-GCM)
- **Networking**: System.Net.Sockets (UDP/TCP)
- **Testing**: xUnit + FluentAssertions

## Project Structure

```
SpiderX/
├── src/
│   ├── SpiderX.Crypto/       # Cryptographic primitives
│   │   ├── SpiderId.cs       # Wallet-like peer identifier
│   │   ├── KeyPair.cs        # Ed25519 key management
│   │   └── Encryption.cs     # AES-GCM encryption
│   │
│   ├── SpiderX.Transport/    # Network transport layer
│   │   ├── ITransport.cs     # Transport abstraction
│   │   ├── UdpTransport.cs   # UDP with reliability
│   │   ├── TcpTransport.cs   # TCP for files
│   │   └── LanDiscovery.cs   # mDNS/broadcast discovery
│   │
│   ├── SpiderX.Core/         # P2P network engine
│   │   ├── SpiderNode.cs     # Main node class
│   │   ├── Peer.cs           # Remote peer representation
│   │   ├── PeerManager.cs    # Connection management
│   │   ├── DHT/              # Kademlia DHT
│   │   └── Messages/         # Protocol messages
│   │
│   ├── SpiderX.Services/     # Application services
│   │   ├── ChatService.cs    # Messaging
│   │   ├── FileTransferService.cs
│   │   └── VoiceService.cs   # Voice calls
│   │
│   └── SpiderX.App/          # MAUI application
│       ├── Views/            # XAML pages
│       ├── ViewModels/       # MVVM view models
│       └── Services/         # App-level services
│
└── tests/
    └── SpiderX.Tests/        # Unit tests
```

## Build & Run Commands

```bash
# Restore dependencies
dotnet restore

# Build entire solution
dotnet build

# Build specific project
dotnet build src/SpiderX.Core

# Run tests
dotnet test

# Run MAUI app (desktop)
cd src/SpiderX.App && dotnet run

# Run on Android
dotnet build -t:Run -f net8.0-android

# Run on iOS (macOS only)
dotnet build -t:Run -f net8.0-ios
```

## Key Concepts

### SpiderId (Identity)
Each peer has a unique identifier derived from their Ed25519 public key:
- Format: `spx1` + Base58Check encoded RIPEMD160(SHA256(publicKey))
- Similar to Bitcoin addresses
- Example: `spx1A1B2C3D4E5F6G7H8I9J0...`

### Peer Discovery
1. **LAN Discovery**: UDP broadcast on port 45678
2. **DHT Lookup**: Kademlia-style distributed hash table
3. **Direct Connect**: Connect by IP:Port or SpiderId

### Message Flow
```
Sender → Encrypt(AES-GCM) → Sign(Ed25519) → Transport → Verify → Decrypt → Receiver
```

### Permission System
Peers must authorize each other before communication:
- `PermissionLevel.Contact` - Can send messages
- `PermissionLevel.FileTransfer` - Can send files
- `PermissionLevel.VoiceCall` - Can make calls

## Code Patterns

### Creating a Node
```csharp
// New random identity
var node = new SpiderNode();

// From seed phrase (deterministic)
var node = SpiderNode.FromSeedPhrase("secret words");

// Start listening
await node.StartAsync();
```

### Sending Messages
```csharp
// Chat message
await node.SendChatAsync(peerId, "Hello!");

// File offer
var fileService = new FileTransferService(node, "/downloads");
await fileService.OfferFileAsync(peerId, "/path/to/file.zip");
```

### Event Handling
```csharp
node.Peers.PeerConnected += (s, e) => {
    Console.WriteLine($"Connected: {e.Peer.Id}");
};

chatService.MessageReceived += (s, e) => {
    Console.WriteLine($"{e.Message.SenderId}: {e.Message.Content}");
};
```

## Common Development Tasks

### Adding a New Message Type
1. Add class in `SpiderX.Core/Messages/Message.cs`
2. Register in `MessageSerializer.Deserialize()`
3. Handle in `PeerManager.HandleMessage()`

### Adding a New Transport
1. Implement `ITransport` interface
2. Implement `IConnection` interface
3. Register in `SpiderNode.StartAsync()`

### Adding a New Service
1. Create in `SpiderX.Services/`
2. Inject `SpiderNode` dependency
3. Subscribe to `Peers.DataReceived` event
4. Add to DI in `MauiProgram.cs`

## Testing Guidelines

```csharp
[Fact]
public void SpiderId_FromPublicKey_GeneratesValidAddress()
{
    var keyPair = KeyPair.Generate();
    var id = SpiderId.FromPublicKey(keyPair.PublicKey);

    id.Address.Should().StartWith("spx1");
    id.Address.Length.Should().BeGreaterThan(30);
}
```

## Important Files to Know

| File | Purpose |
|------|---------|
| `SpiderNode.cs` | Main entry point, orchestrates everything |
| `PeerManager.cs` | Connection lifecycle, message routing |
| `SpiderId.cs` | Identity generation and validation |
| `KeyPair.cs` | Cryptographic key operations |
| `ITransport.cs` | Transport abstraction interface |
| `Message.cs` | All protocol message types |
| `ISpiderXService.cs` | App-level service interface |

## Security Considerations

- **Never** log private keys or decrypted message content
- **Always** verify signatures before processing messages
- **Always** validate SpiderId format before use
- Use `CryptographicOperations.FixedTimeEquals()` for comparisons
- Dispose `KeyPair` objects when done

## Performance Tips

- Use `DeliveryMode.Unreliable` for voice data (lower latency)
- Use `DeliveryMode.Reliable` for chat and file transfer
- Batch DHT lookups when possible
- Use TCP transport for large file transfers

## Debugging

```csharp
// Enable logging
var node = new SpiderNode(options, logger);

// Check peer state
var peer = node.Peers.GetPeer(peerId);
Console.WriteLine($"Connected: {peer?.IsConnected}");
Console.WriteLine($"Latency: {peer?.Latency}ms");

// List all peers
foreach (var p in node.Peers.GetAllPeers())
{
    Console.WriteLine($"{p.Id.Address}: {p.Status}");
}
```

## Contributing Workflow

1. Create feature branch from `main`
2. Write tests first (TDD preferred)
3. Implement feature
4. Run `dotnet test` - all tests must pass
5. Run `dotnet build` - no warnings
6. Create PR with clear description

## Notes for Claude

When working on this codebase:

1. **Understand the Layer Hierarchy**:
   - Transport → Core → Services → App
   - Lower layers should not depend on higher layers

2. **Follow Existing Patterns**:
   - Use `async/await` for all I/O operations
   - Use events for notifications (not callbacks)
   - Use `IDisposable` for cleanup

3. **Security First**:
   - All network data must be encrypted
   - All messages must be signed
   - Validate all external input

4. **Cross-Platform Considerations**:
   - MAUI runs on 4+ platforms
   - Use `MainThread.BeginInvokeOnMainThread()` for UI updates
   - Test file paths work on all platforms

5. **Common Issues**:
   - NAT traversal may fail - provide fallback
   - UDP packets may be lost - implement retry
   - Mobile apps may be backgrounded - handle reconnection

---

## MANDATORY Code Style Rules

**CRITICAL: These rules MUST be followed to avoid CI/CD failures.**

### 1. Always Use Braces for Control Statements
```csharp
// WRONG - will fail CI
if (condition)
    DoSomething();

// CORRECT
if (condition)
{
    DoSomething();
}
```

### 2. Use string.Empty Instead of ""
```csharp
// WRONG - SA1122 violation
string name = "";
return "";

// CORRECT
string name = string.Empty;
return string.Empty;
```

### 3. XML Documentation Format
```csharp
// WRONG - missing period, wrong format
/// <summary>
/// Gets the peer name
/// </summary>

// CORRECT
/// <summary>
/// Gets the peer name.
/// </summary>
public string Name { get; }
```

### 4. Parameter Indentation (SA1116)
```csharp
// WRONG - first parameter on same line
Task.Run(() =>
{
    DoWork();
}, cancellationToken);

// CORRECT - all parameters aligned
Task.Run(
    () =>
    {
        DoWork();
    },
    cancellationToken);
```

### 5. Handle Partial Reads from Streams
```csharp
// WRONG - assumes full read
int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

// CORRECT - loop until all bytes read
int totalRead = 0;
while (totalRead < buffer.Length)
{
    int bytesRead = await stream.ReadAsync(
        buffer,
        totalRead,
        buffer.Length - totalRead,
        cancellationToken);
    if (bytesRead == 0)
    {
        break; // End of stream
    }

    totalRead += bytesRead;
}
```

### 6. MAUI: Avoid Deprecated APIs
```csharp
// WRONG - Application.MainPage is deprecated in .NET 8
Application.Current.MainPage.DisplayAlert(...);

// CORRECT - Use Windows collection
Application.Current?.Windows[0]?.Page?.DisplayAlert(...);

// Or in ViewModels, use injected IDialogService
```

### 7. Avoid Ambiguous Type References
```csharp
// WRONG - ambiguous between Microsoft.Maui.Controls.Application and Microsoft.UI.Xaml.Application
public class App : Application

// CORRECT - use fully qualified name or alias
using MauiApp = Microsoft.Maui.Controls.Application;
public class App : MauiApp
```

### 8. Private Fields Naming Convention
```csharp
// WRONG
private readonly ILogger logger;
private string peerName;

// CORRECT - prefix with underscore
private readonly ILogger _logger;
private string _peerName;
```

### 9. Async Method Naming
```csharp
// WRONG
public async Task Connect();
public async Task<bool> SendMessage();

// CORRECT - suffix with Async
public async Task ConnectAsync();
public async Task<bool> SendMessageAsync();
```

### 10. Dispose Pattern
```csharp
// CORRECT pattern for IDisposable
public class MyClass : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources
        }

        _disposed = true;
    }
}
```

---

## Pre-Commit Checklist

Before committing ANY code, verify:

1. [ ] `dotnet build` - No errors or warnings
2. [ ] `dotnet test` - All tests pass
3. [ ] All `if/else/for/foreach/while` have braces `{}`
4. [ ] No empty string literals `""` - use `string.Empty`
5. [ ] All public members have XML documentation with period at end
6. [ ] Private fields start with `_` underscore
7. [ ] Async methods end with `Async` suffix
8. [ ] No deprecated MAUI APIs (MainPage, etc.)
9. [ ] Stream reads handle partial data correctly
10. [ ] No ambiguous type references
