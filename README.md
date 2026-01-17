<p align="center">
  <img src="https://raw.githubusercontent.com/xjanova/SpiderX/main/docs/logo.png" alt="SpiderX Logo" width="200"/>
</p>

<h1 align="center">SpiderX</h1>

<p align="center">
  <strong>Decentralized P2P Mesh Network for the Free World</strong>
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="#why-spiderx">Why SpiderX</a> •
  <a href="#installation">Installation</a> •
  <a href="#usage">Usage</a> •
  <a href="#architecture">Architecture</a> •
  <a href="#contributing">Contributing</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/MAUI-Cross_Platform-6C3BAA?style=for-the-badge&logo=dotnet" alt="MAUI"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License"/>
  <img src="https://img.shields.io/badge/P2P-Decentralized-orange?style=for-the-badge" alt="P2P"/>
</p>

---

## Overview

**SpiderX** is a revolutionary peer-to-peer mesh network application that enables secure, private communication without relying on central servers. Built on modern cryptography and decentralized architecture, SpiderX empowers users to communicate freely, share files securely, and connect directly with others — anywhere in the world.

> *"Your communication. Your rules. No middleman."*

---

## Why SpiderX?

### The Problem with Traditional Communication

| Issue | Traditional Apps | SpiderX |
|-------|-----------------|---------|
| **Central Control** | Companies control your data | You own your data |
| **Censorship** | Can be blocked by governments | Impossible to censor |
| **Privacy** | Metadata collected & sold | Zero metadata leakage |
| **Single Point of Failure** | Server goes down = no service | No servers to fail |
| **Trust** | Trust the company | Trust only cryptography |

### Built for Freedom

SpiderX was created to solve real-world problems:

- **Journalists & Activists** — Communicate safely in oppressive regimes
- **Privacy-Conscious Users** — Escape mass surveillance and data harvesting
- **Remote Teams** — Share files directly without cloud services
- **Gamers** — Create virtual LANs for LAN-only games over the internet
- **Disaster Response** — Works without internet infrastructure via local mesh
- **Everyone** — Exercise your right to private communication

---

## Features

### Core Features

| Feature | Description |
|---------|-------------|
| **Decentralized Network** | No central servers — peers connect directly via mesh topology |
| **End-to-End Encryption** | Military-grade encryption (X25519 + AES-256-GCM) |
| **Cryptographic Identity** | Unique ID like cryptocurrency wallet (spx1...) |
| **Zero Knowledge** | No registration, no phone number, no email required |
| **Cross-Platform** | Windows, macOS, Linux, iOS, Android |

### Communication

| Feature | Description |
|---------|-------------|
| **Encrypted Chat** | Private messaging with perfect forward secrecy |
| **Voice Calls** | Crystal-clear encrypted voice communication |
| **Group Messaging** | Create private groups with invited members only |
| **Offline Messages** | Messages delivered when recipient comes online |

### File Sharing

| Feature | Description |
|---------|-------------|
| **P2P File Transfer** | BitTorrent-style multi-peer downloads |
| **File Catalog** | Browse and search shared files from contacts |
| **Chunked Transfer** | Resume interrupted downloads |
| **Integrity Verification** | SHA-256 hash verification for all files |

### Virtual LAN

| Feature | Description |
|---------|-------------|
| **Virtual Network** | Create virtual LAN over the internet |
| **LAN Games** | Play LAN-only games with friends worldwide |
| **Low Latency** | Optimized for real-time gaming |
| **Easy Setup** | One-click enable, automatic IP assignment |

### Discovery

| Feature | Description |
|---------|-------------|
| **LAN Discovery** | Auto-discover peers on local network |
| **DHT Lookup** | Find peers globally via distributed hash table |
| **NAT Traversal** | Connect through firewalls automatically |
| **QR Code Sharing** | Add contacts by scanning QR code |

---

## Technology Stack

```
┌─────────────────────────────────────────────────────────────┐
│                    SpiderX Application                       │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Chat      │  │   Files     │  │   Virtual LAN       │  │
│  │   Service   │  │   Service   │  │   Service           │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    SpiderX Core Engine                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │  SpiderNode │  │    DHT      │  │   Peer Manager      │  │
│  │  (P2P Hub)  │  │  (Kademlia) │  │   (Connections)     │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    Transport Layer                           │
│  ┌───────┐  ┌───────┐  ┌───────────┐  ┌─────────────────┐  │
│  │  UDP  │  │  TCP  │  │ WiFi Direct│  │   Bluetooth     │  │
│  └───────┘  └───────┘  └───────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    Cryptography Layer                        │
│  ┌────────────┐  ┌────────────┐  ┌──────────────────────┐  │
│  │  Ed25519   │  │  X25519    │  │  AES-256-GCM         │  │
│  │ (Signing)  │  │  (ECDH)    │  │  (Encryption)        │  │
│  └────────────┘  └────────────┘  └──────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Security Architecture

### Identity & Authentication

```
┌─────────────────────────────────────────────┐
│            Key Generation                    │
│                                              │
│  Seed Phrase ──► Ed25519 Private Key         │
│                        │                     │
│                        ▼                     │
│              Ed25519 Public Key              │
│                        │                     │
│                        ▼                     │
│              SHA-256 + RIPEMD-160            │
│                        │                     │
│                        ▼                     │
│         SpiderId: spx1abc123def...           │
└─────────────────────────────────────────────┘
```

### Message Encryption Flow

```
Sender                                    Receiver
  │                                          │
  │  1. Generate ephemeral X25519 keypair    │
  │                                          │
  │  2. ECDH: ephemeral_priv × receiver_pub  │
  │     = shared_secret                      │
  │                                          │
  │  3. HKDF(shared_secret) = AES key        │
  │                                          │
  │  4. AES-256-GCM encrypt(message)         │
  │                                          │
  │  5. Sign with Ed25519                    │
  │                                          │
  │ ──────────── encrypted packet ─────────► │
  │                                          │
  │                     6. Verify signature  │
  │                     7. ECDH to get key   │
  │                     8. Decrypt message   │
  │                                          │
```

### Security Features

| Feature | Implementation | Purpose |
|---------|---------------|---------|
| **Identity** | Ed25519 | Digital signatures, authentication |
| **Key Exchange** | X25519 ECDH | Establish shared secrets |
| **Encryption** | AES-256-GCM | Authenticated encryption |
| **Hashing** | SHA-256, BLAKE2b | Integrity verification |
| **Forward Secrecy** | Ephemeral keys | Past sessions stay secure |

---

## Installation

### Prerequisites

- **.NET 9.0 SDK** or later
- **Visual Studio 2022** (17.8+) or **VS Code** with C# Dev Kit
- **MAUI Workload** (for mobile builds)

### Quick Start

```bash
# Clone the repository
git clone https://github.com/xjanova/SpiderX.git
cd SpiderX

# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
cd src/SpiderX.App
dotnet run
```

### Platform-Specific Builds

```bash
# Windows
dotnet build -f net9.0-windows10.0.19041.0

# Android
dotnet build -f net9.0-android -t:Run

# iOS (requires Mac)
dotnet build -f net9.0-ios -t:Run

# macOS
dotnet build -f net9.0-maccatalyst
```

---

## Usage

### First Launch

1. **Launch SpiderX** — A unique cryptographic identity is generated automatically
2. **Copy Your ID** — Share your SpiderId (spx1...) with friends
3. **Add Contacts** — Paste friend's ID or scan their QR code
4. **Start Chatting** — All communication is encrypted end-to-end

### Code Examples

#### Create a Node

```csharp
using SpiderX.Core;
using SpiderX.Crypto;

// Create new identity
var node = new SpiderNode();
await node.StartAsync();

Console.WriteLine($"Your ID: {node.Id.Address}");
// Output: spx1A1B2C3D4E5F6G7H8I9J0...

// Or restore from seed phrase
var restored = SpiderNode.FromSeedPhrase("your 24 word seed phrase here");
```

#### Connect to Peers

```csharp
// Auto LAN discovery (automatic)
node.PeerDiscovered += (s, peer) => {
    Console.WriteLine($"Found: {peer.Id.ShortAddress}");
};

// Connect by SpiderId
var peer = await node.ConnectAsync(SpiderId.Parse("spx1..."));

// Connect by IP
var peer = await node.ConnectAsync("192.168.1.100:45678");
```

#### Send Messages

```csharp
// Send encrypted chat
await node.SendChatAsync(peerId, "Hello, secure world!");

// Receive messages
node.MessageReceived += (s, e) => {
    Console.WriteLine($"{e.Sender.ShortAddress}: {e.Content}");
};
```

#### File Sharing

```csharp
var fileService = new P2PFileSharingService(node, downloadPath);

// Share a file
await fileService.ShareFileAsync("/path/to/file.zip", "My shared file");

// Browse peer's catalog
var catalog = await fileService.GetCatalogAsync(peerId);

// Download with multi-peer swarming
await fileService.DownloadFileAsync(fileHash);
```

#### Virtual LAN

```csharp
var vlan = new VirtualLanService(node);

// Enable virtual LAN
await vlan.EnableAsync();
Console.WriteLine($"Virtual IP: {vlan.VirtualIp}"); // 10.0.0.x

// Now launch your LAN game - it will find other players!
```

---

## Architecture

### Project Structure

```
SpiderX/
├── src/
│   ├── SpiderX.Core/              # P2P Network Engine
│   │   ├── SpiderNode.cs          # Main node orchestrator
│   │   ├── Peer.cs                # Peer representation
│   │   ├── PeerManager.cs         # Connection management
│   │   ├── DHT/                   # Kademlia DHT implementation
│   │   ├── Messages/              # Protocol message types
│   │   └── Models/                # Data models
│   │
│   ├── SpiderX.Crypto/            # Cryptographic Operations
│   │   ├── SpiderId.cs            # Unique peer identifier
│   │   ├── KeyPair.cs             # Ed25519 key management
│   │   └── Encryption.cs          # AES-GCM encryption
│   │
│   ├── SpiderX.Transport/         # Network Transports
│   │   ├── ITransport.cs          # Transport abstraction
│   │   ├── UdpTransport.cs        # UDP with reliability
│   │   ├── TcpTransport.cs        # TCP for large files
│   │   └── LanDiscovery.cs        # mDNS/broadcast discovery
│   │
│   ├── SpiderX.Services/          # Application Services
│   │   ├── ChatService.cs         # Messaging
│   │   ├── P2PFileSharingService.cs   # BitTorrent-style files
│   │   ├── VirtualLanService.cs   # Virtual LAN for gaming
│   │   └── VoiceService.cs        # Voice calls
│   │
│   └── SpiderX.App/               # MAUI Cross-Platform App
│       ├── Views/                 # XAML UI pages
│       ├── ViewModels/            # MVVM view models
│       ├── Services/              # App-level services
│       └── Converters/            # XAML value converters
│
└── tests/
    └── SpiderX.Tests/             # Unit & integration tests
```

### Protocol Messages

| Message Type | Purpose |
|-------------|---------|
| `handshake` | Initial connection with key exchange |
| `handshake_ack` | Handshake acknowledgment |
| `ping` / `pong` | Keepalive & latency measurement |
| `find_node` | DHT peer discovery |
| `chat` | Encrypted chat message |
| `file_offer` | File sharing offer |
| `p2p_chunk_request` | Request file chunk |
| `p2p_chunk_response` | File chunk data |
| `catalog_request` | Request peer's file catalog |
| `vlan_*` | Virtual LAN messages |

### Network Ports

| Port | Protocol | Purpose |
|------|----------|---------|
| 45678 | UDP | P2P communication, discovery, DHT |
| 45679 | TCP | Large file transfers, reliable delivery |
| 5353 | UDP | mDNS LAN discovery |

---

## Comparison

### SpiderX vs Others

| Feature | SpiderX | Signal | Telegram | WhatsApp |
|---------|---------|--------|----------|----------|
| **Decentralized** | Yes | No | No | No |
| **No Phone Required** | Yes | No | No | No |
| **No Email Required** | Yes | Yes | No | No |
| **Open Source** | Yes | Partial | No | No |
| **File Sharing P2P** | Yes | No | No | No |
| **Virtual LAN** | Yes | No | No | No |
| **Works Offline (mesh)** | Yes | No | No | No |
| **Censorship Resistant** | Yes | Partial | No | No |
| **Metadata Privacy** | Yes | Partial | No | No |

---

## Use Cases

### For Individuals

- **Private Messaging** — Chat without Big Tech watching
- **Secure File Sharing** — Share files directly, no cloud needed
- **Gaming** — Play LAN games with friends anywhere in the world

### For Organizations

- **Secure Internal Communication** — No data leaves your network
- **Field Operations** — Works without internet (mesh mode)
- **Whistleblower Protection** — Anonymous, untraceable communication

### For Developers

- **Build P2P Apps** — Use SpiderX.Core as foundation
- **Custom Services** — Add your own services on top
- **IoT Networks** — Connect devices in mesh topology

---

## Roadmap

### Version 1.0 (Current)
- [x] Core P2P engine
- [x] End-to-end encryption
- [x] Chat messaging
- [x] P2P file sharing
- [x] Virtual LAN
- [x] LAN discovery
- [x] Cross-platform MAUI app

### Version 1.1 (Planned)
- [ ] Voice calls
- [ ] Video calls
- [ ] Group chats
- [ ] Message reactions

### Version 2.0 (Future)
- [ ] Screen sharing
- [ ] Encrypted cloud backup (optional)
- [ ] Browser extension
- [ ] Desktop widgets
- [ ] TURN server support
- [ ] Tor integration

---

## Contributing

We welcome contributions from the community!

### How to Contribute

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Write unit tests for new features
- Update documentation as needed
- Keep commits atomic and well-described

### Report Issues

Found a bug? Have a feature request? [Open an issue](https://github.com/xjanova/SpiderX/issues)

---

## License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

## Acknowledgments

- **libsodium** — Cryptographic primitives
- **.NET MAUI** — Cross-platform UI framework
- **Kademlia** — DHT algorithm inspiration
- **BitTorrent** — P2P file sharing concepts
- **Signal Protocol** — Secure messaging inspiration

---

<p align="center">
  <strong>SpiderX — Communication Without Compromise</strong>
</p>

<p align="center">
  <em>Decentralized. Encrypted. Free.</em>
</p>

<p align="center">
  Built with love for a freer world
</p>

<p align="center">
  <a href="https://github.com/xjanova/SpiderX/stargazers">Star us on GitHub</a>
</p>
