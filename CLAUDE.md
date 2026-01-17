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
