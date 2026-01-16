# SpiderX - P2P Mesh Network

A decentralized peer-to-peer mesh network application for secure communication across devices without central servers. Similar to BitTorrent but for real-time communication.

## Features

- **Decentralized P2P Network** - No central server required, peers connect directly
- **Cryptographic Identity** - Each user has a unique ID (like a crypto wallet address)
- **End-to-End Encryption** - All communications are encrypted using modern cryptography
- **Multi-Transport** - Supports UDP, TCP, WiFi Direct, and Bluetooth
- **LAN Discovery** - Automatically discover peers on the same network
- **NAT Traversal** - Connect through NAT using hole punching
- **Cross-Platform** - Works on Windows, macOS, Linux, iOS, and Android

## Applications

- **Chat** - Send encrypted messages to contacts
- **File Transfer** - Share files directly between devices
- **Voice Calls** - Make encrypted voice calls
- **Contact Management** - Permission-based contact system

## Architecture

```
SpiderX/
├── src/
│   ├── SpiderX.Core/           # P2P Network Engine
│   │   ├── SpiderNode.cs       # Main node class
│   │   ├── Peer.cs             # Peer representation
│   │   ├── PeerManager.cs      # Connection management
│   │   ├── DHT/                # Distributed Hash Table
│   │   └── Messages/           # Protocol messages
│   │
│   ├── SpiderX.Crypto/         # Cryptographic Identity
│   │   ├── SpiderId.cs         # Unique peer ID (like wallet address)
│   │   ├── KeyPair.cs          # Ed25519 key pair
│   │   └── Encryption.cs       # AES-GCM encryption
│   │
│   ├── SpiderX.Transport/      # Network Transports
│   │   ├── ITransport.cs       # Transport interface
│   │   ├── UdpTransport.cs     # UDP with reliability
│   │   ├── TcpTransport.cs     # TCP transport
│   │   └── LanDiscovery.cs     # mDNS/Broadcast discovery
│   │
│   ├── SpiderX.Services/       # Application Services
│   │   ├── ChatService.cs      # Chat functionality
│   │   ├── FileTransferService.cs  # File transfer
│   │   └── VoiceService.cs     # Voice calls
│   │
│   └── SpiderX.App/            # MAUI Application
│       ├── Views/              # UI pages
│       ├── ViewModels/         # MVVM view models
│       └── Services/           # App services
│
└── tests/
    └── SpiderX.Tests/          # Unit tests
```

## How It Works

### Identity
Each peer generates an Ed25519 key pair. The public key is hashed to create a unique SpiderId (similar to a cryptocurrency address):

```
spx1[base58check encoded hash]
```

Example: `spx1A1B2C3D4E5F6G7H8I9J0...`

### Network Discovery
1. **LAN Discovery** - Peers announce themselves via UDP broadcast/multicast
2. **DHT Lookup** - Kademlia-style distributed hash table for finding peers
3. **NAT Punch-through** - UDP hole punching to connect through firewalls

### Security
- **End-to-End Encryption** - All messages encrypted using ECDH + AES-256-GCM
- **Message Signing** - All messages are signed with Ed25519
- **Permission System** - Users must authorize contacts before communication

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- For mobile: MAUI workload installed

### Build

```bash
# Clone the repository
git clone https://github.com/your-username/spiderx.git
cd spiderx

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test
```

### Run the Application

```bash
# Run desktop app (Windows/macOS/Linux)
cd src/SpiderX.App
dotnet run

# Run on Android
dotnet build -t:Run -f net8.0-android

# Run on iOS
dotnet build -t:Run -f net8.0-ios
```

## Usage

### Create a Node

```csharp
using SpiderX.Core;

// Create a new node with random identity
var node = new SpiderNode();

// Or create from seed phrase (deterministic)
var node = SpiderNode.FromSeedPhrase("your secret seed phrase");

// Start the node
await node.StartAsync();

// Your unique ID
Console.WriteLine($"Your ID: {node.Id.Address}");
```

### Connect to Peers

```csharp
// Connect by endpoint
var peer = await node.ConnectAsync("192.168.1.100:45678");

// Connect by SpiderId
var peer = await node.ConnectAsync(SpiderId.Parse("spx1..."));

// LAN discovery is automatic!
```

### Send Messages

```csharp
// Chat
await node.SendChatAsync(peerId, "Hello, World!");

// Request contact permission
await node.RequestPermissionAsync(peerId, "contact");
```

### File Transfer

```csharp
var fileService = new FileTransferService(node, "/downloads");

// Send a file
await fileService.OfferFileAsync(peerId, "/path/to/file.zip");

// Receive file offers
fileService.FileOfferReceived += (s, e) => {
    Console.WriteLine($"File offer: {e.Transfer.FileName}");
    fileService.AcceptFileAsync(e.Transfer.Id);
};
```

## Protocol

### Message Types

| Type | Description |
|------|-------------|
| `handshake` | Initial connection with public key exchange |
| `handshake_ack` | Handshake acknowledgment |
| `ping/pong` | Keepalive messages |
| `find_node` | DHT peer lookup |
| `chat` | Chat message |
| `file_offer` | File transfer offer |
| `file_chunk` | File data chunk |
| `voice_data` | Voice call audio |
| `permission_request` | Contact/call request |

### Ports

| Port | Protocol | Purpose |
|------|----------|---------|
| 45678 | UDP | P2P communication, discovery |
| 45679 | TCP | File transfer, reliable delivery |

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Roadmap

- [ ] Video calls
- [ ] Group chat
- [ ] Screen sharing
- [ ] Desktop widgets
- [ ] Browser extension
- [ ] TURN server support for restricted networks
- [ ] End-to-end encrypted file storage
