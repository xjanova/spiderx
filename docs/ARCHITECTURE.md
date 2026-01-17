# SpiderX Architecture

เอกสาร Architecture ของ SpiderX P2P Mesh Network

## สารบัญ

1. [ภาพรวมระบบ](#ภาพรวมระบบ)
2. [Layer Architecture](#layer-architecture)
3. [Identity System](#identity-system)
4. [Network Layer](#network-layer)
5. [P2P Engine](#p2p-engine)
6. [Security Architecture](#security-architecture)
7. [Data Flow](#data-flow)
8. [Scalability](#scalability)

---

## ภาพรวมระบบ

SpiderX เป็นระบบ P2P Mesh Network แบบ Decentralized ที่ไม่ต้องมี Central Server

```
                    ┌─────────┐
                    │  Peer A │
                    └────┬────┘
                         │
        ┌────────────────┼────────────────┐
        │                │                │
        ▼                ▼                ▼
   ┌─────────┐      ┌─────────┐      ┌─────────┐
   │  Peer B │◄────►│  Peer C │◄────►│  Peer D │
   └─────────┘      └─────────┘      └─────────┘
        │                │                │
        └────────────────┼────────────────┘
                         │
                    ┌────┴────┐
                    │  Peer E │
                    └─────────┘

            Mesh Topology - ทุก node เชื่อมต่อกัน
```

### Design Principles

1. **Decentralized** - ไม่มี single point of failure
2. **Secure by Default** - E2E encryption ทุก message
3. **Privacy First** - ผู้ใช้ควบคุม identity และ data
4. **Cross-Platform** - ทำงานได้ทุก platform
5. **Offline Capable** - ทำงานได้ใน LAN โดยไม่ต้องมี internet

---

## Layer Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Application Layer                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │  MAUI Views  │  │  ViewModels  │  │  App Services│          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
├─────────────────────────────────────────────────────────────────┤
│                         Service Layer                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ ChatService  │  │ FileService  │  │ VoiceService │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
├─────────────────────────────────────────────────────────────────┤
│                          Core Layer                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │  SpiderNode  │  │ PeerManager  │  │     DHT      │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
├─────────────────────────────────────────────────────────────────┤
│                      Infrastructure Layer                        │
│  ┌─────────────────────────┐  ┌─────────────────────────┐      │
│  │      Transport          │  │       Crypto            │      │
│  │  UDP │ TCP │ Bluetooth  │  │  KeyPair │ Encryption   │      │
│  └─────────────────────────┘  └─────────────────────────┘      │
└─────────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

| Layer | Responsibility | Projects |
|-------|----------------|----------|
| **Application** | UI, User interaction | SpiderX.App |
| **Service** | Business logic, Features | SpiderX.Services |
| **Core** | P2P networking, Peer management | SpiderX.Core |
| **Infrastructure** | Transport, Crypto | SpiderX.Transport, SpiderX.Crypto |

---

## Identity System

### SpiderId Generation

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Ed25519    │     │   SHA-256    │     │  RIPEMD160   │
│  Public Key  │────►│    Hash      │────►│    Hash      │
│   (32 bytes) │     │  (32 bytes)  │     │  (20 bytes)  │
└──────────────┘     └──────────────┘     └──────────────┘
                                                 │
                                                 ▼
                                          ┌──────────────┐
                                          │ Base58Check  │
                                          │   Encode     │
                                          └──────────────┘
                                                 │
                                                 ▼
                                          ┌──────────────┐
                                          │   "spx1" +   │
                                          │   Address    │
                                          └──────────────┘

Result: spx1A1B2C3D4E5F6G7H8I9J0...
```

### Key Derivation

```csharp
// จาก Seed Phrase (deterministic)
Seed Phrase → PBKDF2 → 32-byte Seed → Ed25519 KeyPair

// จาก Random (non-deterministic)
RandomNumberGenerator → 32-byte Seed → Ed25519 KeyPair
```

### Identity Storage

```
┌─────────────────────────────────────┐
│           Secure Storage            │
│  ┌─────────────────────────────┐   │
│  │  Private Key (encrypted)    │   │
│  │  + AES-256-GCM              │   │
│  │  + Key from user password   │   │
│  └─────────────────────────────┘   │
│                                     │
│  Platforms:                         │
│  - iOS: Keychain                    │
│  - Android: KeyStore                │
│  - Windows: DPAPI                   │
│  - macOS: Keychain                  │
└─────────────────────────────────────┘
```

---

## Network Layer

### Transport Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    ITransport Interface                  │
└─────────────────────────────────────────────────────────┘
          │              │              │           │
          ▼              ▼              ▼           ▼
    ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐
    │   UDP    │  │   TCP    │  │Bluetooth │  │WiFi Direct│
    │Transport │  │Transport │  │Transport │  │Transport │
    └──────────┘  └──────────┘  └──────────┘  └──────────┘
         │              │              │           │
         ▼              ▼              ▼           ▼
    ┌─────────────────────────────────────────────────────┐
    │                  Physical Network                    │
    └─────────────────────────────────────────────────────┘
```

### Transport Selection

| Transport | Use Case | Pros | Cons |
|-----------|----------|------|------|
| **UDP** | Real-time (voice, video) | Low latency | Unreliable |
| **TCP** | File transfer, Chat | Reliable | Higher latency |
| **Bluetooth** | Nearby devices | No internet needed | Short range |
| **WiFi Direct** | LAN devices | Fast | Requires WiFi |

### Connection Flow

```
┌─────────┐                                       ┌─────────┐
│ Peer A  │                                       │ Peer B  │
└────┬────┘                                       └────┬────┘
     │                                                 │
     │  1. UDP Handshake Request                       │
     │  ─────────────────────────────────────────────► │
     │  (PublicKey, Nonce)                             │
     │                                                 │
     │  2. UDP Handshake Response                      │
     │  ◄───────────────────────────────────────────── │
     │  (PublicKey, Nonce, Signature)                  │
     │                                                 │
     │  3. Verify Signature                            │
     │  ════════════════                               │
     │                                                 │
     │  4. Handshake Acknowledgment                    │
     │  ─────────────────────────────────────────────► │
     │  (Signature)                                    │
     │                                                 │
     │  ═══════════════════════════════════════════    │
     │        Connection Established                   │
     │  ═══════════════════════════════════════════    │
     │                                                 │
```

### NAT Traversal

```
┌─────────────────────────────────────────────────────────────────┐
│                        NAT Punch-through                         │
│                                                                  │
│  ┌────────┐         ┌────────┐         ┌────────┐              │
│  │ Peer A │         │  NAT   │         │ Peer B │              │
│  │(Private)│        │Router  │         │(Private)│              │
│  └────┬───┘         └────┬───┘         └────┬───┘              │
│       │                  │                  │                   │
│       │  1. Send to B    │                  │                   │
│       │─────────────────►│                  │                   │
│       │                  │  2. Creates      │                   │
│       │                  │  port mapping    │                   │
│       │                  │                  │                   │
│       │                  │  3. Forward      │                   │
│       │                  │─────────────────►│                   │
│       │                  │                  │                   │
│       │                  │  4. Reply        │                   │
│       │◄─────────────────│◄─────────────────│                   │
│       │                  │                  │                   │
│       │     Direct P2P Connection           │                   │
│       │◄═══════════════════════════════════►│                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### LAN Discovery

```
┌─────────────────────────────────────────────────────────────────┐
│                      LAN Discovery Flow                          │
│                                                                  │
│  1. Broadcast Announcement (UDP port 45678)                      │
│     ┌─────────┐      Broadcast      ┌─────────┐                 │
│     │ Peer A  │ ──────────────────► │   LAN   │                 │
│     └─────────┘                     └─────────┘                 │
│                                          │                       │
│  2. All peers in LAN receive             │                       │
│     ┌─────────┐  ┌─────────┐  ┌─────────┐                       │
│     │ Peer B  │  │ Peer C  │  │ Peer D  │                       │
│     └─────────┘  └─────────┘  └─────────┘                       │
│          │            │            │                             │
│  3. Respond with own info           │                             │
│          │            │            │                             │
│          └────────────┼────────────┘                             │
│                       ▼                                          │
│                  ┌─────────┐                                     │
│                  │ Peer A  │ (now knows B, C, D)                 │
│                  └─────────┘                                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## P2P Engine

### DHT (Distributed Hash Table)

ใช้ **Kademlia** algorithm:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Kademlia Routing Table                        │
│                                                                  │
│  Local Node ID: 1010...                                          │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ K-Bucket 0 (distance 1)     │ Nodes: [0010..., 0110...]│    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ K-Bucket 1 (distance 2-3)   │ Nodes: [1110..., 1000...]│    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ K-Bucket 2 (distance 4-7)   │ Nodes: [...]             │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ ...                         │                          │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ K-Bucket 159 (distance 2^159)│ Nodes: [...]            │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  K = 20 (max nodes per bucket)                                   │
│  XOR distance = localId XOR remoteId                             │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Node Lookup Algorithm

```
FindNode(targetId):
    1. Get K closest nodes from local routing table
    2. In parallel, query each node for their K closest to target
    3. Merge results, keep K closest
    4. Repeat until no closer nodes found
    5. Return K closest nodes to target
```

### Peer Manager State Machine

```
                    ┌──────────────┐
                    │  Discovered  │
                    └──────┬───────┘
                           │ Connect
                           ▼
                    ┌──────────────┐
         ┌─────────│  Connecting  │─────────┐
         │ Timeout └──────────────┘ Success │
         ▼                                   ▼
┌──────────────┐                    ┌──────────────┐
│  Unreachable │                    │  Connected   │
└──────────────┘                    └──────┬───────┘
         ▲                                 │
         │ Disconnect                      │ Handshake
         │                                 ▼
         │                          ┌──────────────┐
         └──────────────────────────│ Authenticated│
                                    └──────┬───────┘
                                           │ Authorize
                                           ▼
                                    ┌──────────────┐
                                    │  Authorized  │
                                    └──────────────┘
```

---

## Security Architecture

### Encryption Layer

```
┌─────────────────────────────────────────────────────────────────┐
│                    Message Encryption Flow                       │
│                                                                  │
│  Plaintext                                                       │
│      │                                                           │
│      ▼                                                           │
│  ┌───────────────┐                                              │
│  │ ECDH Key      │  Sender Private Key + Recipient Public Key   │
│  │ Agreement     │  ────────────────────────────────────────►   │
│  └───────────────┘  = Shared Secret                             │
│      │                                                           │
│      ▼                                                           │
│  ┌───────────────┐                                              │
│  │ AES-256-GCM   │  Shared Secret + Random Nonce                │
│  │ Encrypt       │  ────────────────────────────────────────►   │
│  └───────────────┘  = Ciphertext + Auth Tag                     │
│      │                                                           │
│      ▼                                                           │
│  ┌───────────────┐                                              │
│  │ Ed25519 Sign  │  Sender Private Key                          │
│  │               │  ────────────────────────────────────────►   │
│  └───────────────┘  = Signature                                 │
│      │                                                           │
│      ▼                                                           │
│  Encrypted Envelope:                                             │
│  { sender_id, sender_public_key, nonce, ciphertext, tag, sig }  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Permission System

```
┌─────────────────────────────────────────────────────────────────┐
│                      Permission Levels                           │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ None         │ No communication allowed                   │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Discovered   │ Can see peer exists                        │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Contact      │ Can send/receive messages                  │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ FileTransfer │ Contact + can send/receive files           │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ VoiceCall    │ FileTransfer + can make voice calls        │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Full         │ All permissions                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Permission Request Flow:                                        │
│  A ──── PermissionRequest ────► B                               │
│  A ◄─── PermissionResponse ──── B (accept/reject)               │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Threat Model

| Threat | Mitigation |
|--------|------------|
| MITM Attack | E2E encryption, signature verification |
| Impersonation | Ed25519 signatures on all messages |
| Replay Attack | Nonce in every message, timestamp validation |
| DoS | Rate limiting, connection limits |
| Privacy Leak | No central server, encrypted metadata |

---

## Data Flow

### Chat Message Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                       Chat Message Flow                           │
│                                                                   │
│  User A                    Network                    User B      │
│  ───────                   ───────                    ───────     │
│     │                                                    │        │
│     │  1. Type message                                   │        │
│     │  ══════════════                                    │        │
│     │     │                                              │        │
│     │     ▼                                              │        │
│     │  ┌─────────────┐                                   │        │
│     │  │ ChatService │                                   │        │
│     │  └──────┬──────┘                                   │        │
│     │         │ 2. Create ChatMessage                    │        │
│     │         ▼                                          │        │
│     │  ┌─────────────┐                                   │        │
│     │  │ PeerManager │                                   │        │
│     │  └──────┬──────┘                                   │        │
│     │         │ 3. Encrypt + Sign                        │        │
│     │         ▼                                          │        │
│     │  ┌─────────────┐                                   │        │
│     │  │  Transport  │                                   │        │
│     │  └──────┬──────┘                                   │        │
│     │         │ 4. Send via UDP/TCP                      │        │
│     │         │─────────────────────────────────────────►│        │
│     │         │                                          │        │
│     │         │                    5. Receive            │        │
│     │         │                    ┌─────────────┐       │        │
│     │         │                    │  Transport  │       │        │
│     │         │                    └──────┬──────┘       │        │
│     │         │                           │ 6. Verify + Decrypt   │
│     │         │                    ┌──────┴──────┐       │        │
│     │         │                    │ PeerManager │       │        │
│     │         │                    └──────┬──────┘       │        │
│     │         │                           │ 7. Dispatch           │
│     │         │                    ┌──────┴──────┐       │        │
│     │         │                    │ ChatService │       │        │
│     │         │                    └─────────────┘       │        │
│     │         │                           │              │        │
│     │         │                           ▼              │        │
│     │         │                    8. Display message    │        │
│     │         │                    ══════════════════    │        │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

### File Transfer Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                      File Transfer Flow                           │
│                                                                   │
│  Sender                                              Receiver     │
│  ───────                                             ─────────    │
│     │                                                    │        │
│     │  1. FileOffer (filename, size, hash)               │        │
│     │────────────────────────────────────────────────────►        │
│     │                                                    │        │
│     │                          2. FileResponse (accept)  │        │
│     │◄────────────────────────────────────────────────────        │
│     │                                                    │        │
│     │  3. FileChunk [0] (data, offset, sequence)         │        │
│     │────────────────────────────────────────────────────►        │
│     │                                                    │        │
│     │                          4. ChunkAck [0]           │        │
│     │◄────────────────────────────────────────────────────        │
│     │                                                    │        │
│     │  5. FileChunk [1]                                  │        │
│     │────────────────────────────────────────────────────►        │
│     │                                                    │        │
│     │                          6. ChunkAck [1]           │        │
│     │◄────────────────────────────────────────────────────        │
│     │                                                    │        │
│     │  ... (repeat until complete)                       │        │
│     │                                                    │        │
│     │  N. FileComplete (final hash verification)         │        │
│     │────────────────────────────────────────────────────►        │
│     │                                                    │        │
│     │                          N+1. TransferComplete     │        │
│     │◄────────────────────────────────────────────────────        │
│                                                                   │
│  Chunk Size: 64KB (configurable)                                  │
│  Parallel Chunks: Up to 4                                         │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Scalability

### Network Scaling

```
┌─────────────────────────────────────────────────────────────────┐
│                    Network Scaling Strategy                      │
│                                                                  │
│  Small Network (< 100 peers):                                    │
│  - Full mesh connectivity possible                               │
│  - Direct connections to all peers                               │
│                                                                  │
│  Medium Network (100-1000 peers):                                │
│  - DHT for peer discovery                                        │
│  - Connect to subset of peers                                    │
│  - Relay through connected peers                                 │
│                                                                  │
│  Large Network (> 1000 peers):                                   │
│  - DHT mandatory                                                 │
│  - Super-nodes for relay                                         │
│  - Geographic clustering                                         │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │        Connection Limits                                │    │
│  │  Max Peers: 50 (configurable)                           │    │
│  │  Min Peers: 8 (for resilience)                          │    │
│  │  DHT Bucket Size (K): 20                                │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Resource Management

| Resource | Limit | Strategy |
|----------|-------|----------|
| Connections | 50 max | LRU eviction |
| Memory per peer | ~10KB | Streaming for large data |
| Bandwidth | Rate limited | Fair queuing |
| Storage | User configurable | Automatic cleanup |

### Performance Targets

| Metric | Target |
|--------|--------|
| Message latency | < 100ms (LAN), < 500ms (WAN) |
| File transfer | > 10 MB/s (LAN) |
| Voice latency | < 150ms |
| Peer discovery | < 5s (LAN), < 30s (WAN) |
| Memory usage | < 100MB |

---

## Future Architecture

### Planned Enhancements

1. **WebRTC Transport** - Browser support
2. **TURN Server Support** - For restricted networks
3. **Group Messaging** - Multi-party encryption
4. **Video Calls** - VP8/VP9 codec support
5. **Offline Messages** - Store-and-forward via trusted peers

### Extension Points

```
┌─────────────────────────────────────────────────────────────────┐
│                      Plugin Architecture                         │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    SpiderNode                           │    │
│  │  ┌───────────┐  ┌───────────┐  ┌───────────┐           │    │
│  │  │ITransport │  │ IService  │  │IDiscovery │           │    │
│  │  │  Plugin   │  │  Plugin   │  │  Plugin   │           │    │
│  │  └───────────┘  └───────────┘  └───────────┘           │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  Example Plugins:                                                │
│  - WebRtcTransport : ITransport                                  │
│  - VideoService : IService                                       │
│  - DnsDiscovery : IDiscovery                                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```
