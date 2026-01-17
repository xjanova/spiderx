# SpiderX Protocol Specification

SpiderX P2P Protocol Specification v1.0

## สารบัญ

1. [Overview](#overview)
2. [Wire Format](#wire-format)
3. [Handshake Protocol](#handshake-protocol)
4. [Message Types](#message-types)
5. [DHT Protocol](#dht-protocol)
6. [NAT Traversal](#nat-traversal)
7. [Security](#security)

---

## Overview

SpiderX ใช้ custom binary protocol สำหรับ P2P communication

### Design Goals

- **Efficiency** - Compact binary format
- **Security** - All messages signed and encrypted
- **Extensibility** - Version field for future updates
- **Cross-platform** - Little-endian byte order

### Ports

| Port | Protocol | Purpose |
|------|----------|---------|
| 45678 | UDP | P2P messaging, discovery, voice |
| 45679 | TCP | File transfer, reliable messaging |

---

## Wire Format

### Packet Structure

```
┌──────────────────────────────────────────────────────────────────┐
│                        SpiderX Packet                             │
├──────────┬──────────┬──────────┬─────────────────────────────────┤
│  Magic   │ Version  │  Flags   │          Length                 │
│ (4 bytes)│ (1 byte) │ (1 byte) │         (4 bytes)               │
├──────────┴──────────┴──────────┴─────────────────────────────────┤
│                          Payload                                  │
│                        (variable)                                 │
├──────────────────────────────────────────────────────────────────┤
│                      Checksum (CRC32)                             │
│                         (4 bytes)                                 │
└──────────────────────────────────────────────────────────────────┘
```

### Header Fields

| Field | Size | Description |
|-------|------|-------------|
| Magic | 4 bytes | `0x53505858` ("SPXX") |
| Version | 1 byte | Protocol version (1) |
| Flags | 1 byte | Packet flags |
| Length | 4 bytes | Payload length (little-endian) |
| Payload | variable | Message content |
| Checksum | 4 bytes | CRC32 of header + payload |

### Flags

```
Bit 0: Encrypted (1 = encrypted payload)
Bit 1: Compressed (1 = compressed payload)
Bit 2: Fragmented (1 = part of larger message)
Bit 3: Acknowledgment required
Bit 4-7: Reserved
```

### Encrypted Payload Format

```
┌──────────────────────────────────────────────────────────────────┐
│                    Encrypted Payload                              │
├──────────────────────────────────────────────────────────────────┤
│                    Sender ID (20 bytes)                           │
├──────────────────────────────────────────────────────────────────┤
│                  Sender Public Key (32 bytes)                     │
├──────────────────────────────────────────────────────────────────┤
│                      Nonce (12 bytes)                             │
├──────────────────────────────────────────────────────────────────┤
│                    Ciphertext (variable)                          │
├──────────────────────────────────────────────────────────────────┤
│                   Auth Tag (16 bytes)                             │
├──────────────────────────────────────────────────────────────────┤
│                   Signature (64 bytes)                            │
└──────────────────────────────────────────────────────────────────┘
```

---

## Handshake Protocol

### Connection Establishment

```
┌────────┐                                          ┌────────┐
│ Node A │                                          │ Node B │
└───┬────┘                                          └───┬────┘
    │                                                   │
    │  1. HANDSHAKE_INIT                                │
    │  ───────────────────────────────────────────────► │
    │  { version, public_key, nonce_a, timestamp }      │
    │                                                   │
    │  2. HANDSHAKE_RESPONSE                            │
    │  ◄─────────────────────────────────────────────── │
    │  { version, public_key, nonce_b, nonce_a,         │
    │    timestamp, signature }                         │
    │                                                   │
    │  3. Verify signature(nonce_a || nonce_b)          │
    │  ════════════════════════════════                 │
    │                                                   │
    │  4. HANDSHAKE_ACK                                 │
    │  ───────────────────────────────────────────────► │
    │  { nonce_b, signature }                           │
    │                                                   │
    │  5. Derive shared secret                          │
    │  ════════════════════════════════                 │
    │  shared_secret = ECDH(private_key, other_public)  │
    │                                                   │
    │  ═══════════════════════════════════════════════  │
    │              Encrypted Channel Ready              │
    │  ═══════════════════════════════════════════════  │
    │                                                   │
```

### Handshake Messages

#### HANDSHAKE_INIT (type: 0x01)

```json
{
  "type": "handshake",
  "version": 1,
  "public_key": "base64...",
  "nonce": "base64...",
  "timestamp": 1705420800000,
  "user_agent": "SpiderX/1.0"
}
```

#### HANDSHAKE_RESPONSE (type: 0x02)

```json
{
  "type": "handshake_ack",
  "version": 1,
  "public_key": "base64...",
  "nonce": "base64...",
  "echo_nonce": "base64...",
  "timestamp": 1705420800100,
  "signature": "base64..."
}
```

#### HANDSHAKE_ACK (type: 0x03)

```json
{
  "type": "handshake_complete",
  "echo_nonce": "base64...",
  "signature": "base64..."
}
```

---

## Message Types

### Control Messages

| Type | Code | Description |
|------|------|-------------|
| PING | 0x10 | Keepalive ping |
| PONG | 0x11 | Keepalive response |
| DISCONNECT | 0x12 | Graceful disconnect |

#### PING

```json
{
  "type": "ping",
  "timestamp": 1705420800000,
  "seq": 12345
}
```

#### PONG

```json
{
  "type": "pong",
  "timestamp": 1705420800050,
  "echo_seq": 12345
}
```

---

### Permission Messages

| Type | Code | Description |
|------|------|-------------|
| PERMISSION_REQUEST | 0x20 | Request permission |
| PERMISSION_RESPONSE | 0x21 | Permission response |
| PERMISSION_REVOKE | 0x22 | Revoke permission |

#### PERMISSION_REQUEST

```json
{
  "type": "permission_request",
  "request_id": "uuid",
  "permission_type": "contact|file|call",
  "display_name": "User Name",
  "message": "Optional message"
}
```

#### PERMISSION_RESPONSE

```json
{
  "type": "permission_response",
  "request_id": "uuid",
  "granted": true,
  "permissions": ["contact", "file"],
  "expires_at": 1705507200000
}
```

---

### Chat Messages

| Type | Code | Description |
|------|------|-------------|
| CHAT | 0x30 | Text message |
| CHAT_ACK | 0x31 | Delivery acknowledgment |
| CHAT_READ | 0x32 | Read receipt |

#### CHAT

```json
{
  "type": "chat",
  "id": "uuid",
  "recipient_id": "spx1...",
  "content": "Hello!",
  "reply_to": null,
  "timestamp": 1705420800000
}
```

#### CHAT with Attachment

```json
{
  "type": "chat",
  "id": "uuid",
  "recipient_id": "spx1...",
  "content": "",
  "attachment": {
    "type": "image/png",
    "size": 12345,
    "hash": "sha256...",
    "data": "base64..."
  },
  "timestamp": 1705420800000
}
```

---

### File Transfer Messages

| Type | Code | Description |
|------|------|-------------|
| FILE_OFFER | 0x40 | Offer file transfer |
| FILE_RESPONSE | 0x41 | Accept/reject offer |
| FILE_CHUNK | 0x42 | File data chunk |
| FILE_CHUNK_ACK | 0x43 | Chunk acknowledgment |
| FILE_COMPLETE | 0x44 | Transfer complete |
| FILE_CANCEL | 0x45 | Cancel transfer |

#### FILE_OFFER

```json
{
  "type": "file_offer",
  "transfer_id": "uuid",
  "file_name": "document.pdf",
  "file_size": 1048576,
  "file_hash": "sha256...",
  "mime_type": "application/pdf",
  "chunk_size": 65536
}
```

#### FILE_RESPONSE

```json
{
  "type": "file_response",
  "transfer_id": "uuid",
  "accepted": true,
  "resume_offset": 0
}
```

#### FILE_CHUNK

```
┌──────────────────────────────────────────────────────────────────┐
│                       FILE_CHUNK (Binary)                         │
├──────────────────────────────────────────────────────────────────┤
│                    Transfer ID (16 bytes UUID)                    │
├──────────────────────────────────────────────────────────────────┤
│                    Sequence Number (4 bytes)                      │
├──────────────────────────────────────────────────────────────────┤
│                      Offset (8 bytes)                             │
├──────────────────────────────────────────────────────────────────┤
│                    Chunk Length (4 bytes)                         │
├──────────────────────────────────────────────────────────────────┤
│                      Chunk Data (variable)                        │
├──────────────────────────────────────────────────────────────────┤
│                    Chunk Hash (32 bytes SHA256)                   │
└──────────────────────────────────────────────────────────────────┘
```

#### FILE_CHUNK_ACK

```json
{
  "type": "file_chunk_ack",
  "transfer_id": "uuid",
  "sequence": 5,
  "status": "ok|resend"
}
```

---

### Voice Messages

| Type | Code | Description |
|------|------|-------------|
| VOICE_CALL | 0x50 | Initiate call |
| VOICE_ANSWER | 0x51 | Answer call |
| VOICE_REJECT | 0x52 | Reject call |
| VOICE_END | 0x53 | End call |
| VOICE_DATA | 0x54 | Audio data |

#### VOICE_CALL

```json
{
  "type": "voice_call",
  "call_id": "uuid",
  "codec": "opus",
  "sample_rate": 48000,
  "channels": 1
}
```

#### VOICE_DATA (Binary, UDP)

```
┌──────────────────────────────────────────────────────────────────┐
│                       VOICE_DATA (Binary)                         │
├──────────────────────────────────────────────────────────────────┤
│                      Call ID (16 bytes UUID)                      │
├──────────────────────────────────────────────────────────────────┤
│                    Sequence Number (4 bytes)                      │
├──────────────────────────────────────────────────────────────────┤
│                      Timestamp (4 bytes)                          │
├──────────────────────────────────────────────────────────────────┤
│                    Audio Length (2 bytes)                         │
├──────────────────────────────────────────────────────────────────┤
│                      Audio Data (variable)                        │
│                    (Opus encoded, ~64 bytes per 20ms)             │
└──────────────────────────────────────────────────────────────────┘
```

---

## DHT Protocol

### Kademlia Operations

| Type | Code | Description |
|------|------|-------------|
| FIND_NODE | 0x60 | Find closest nodes |
| FIND_NODE_RESPONSE | 0x61 | Return closest nodes |
| STORE | 0x62 | Store value |
| FIND_VALUE | 0x63 | Find stored value |
| FIND_VALUE_RESPONSE | 0x64 | Return value or nodes |

#### FIND_NODE

```json
{
  "type": "find_node",
  "target_id": "spx1...",
  "request_id": "uuid"
}
```

#### FIND_NODE_RESPONSE

```json
{
  "type": "find_node_response",
  "request_id": "uuid",
  "nodes": [
    {
      "id": "spx1...",
      "public_key": "base64...",
      "endpoints": [
        { "address": "192.168.1.1", "port": 45678, "type": "udp" }
      ],
      "last_seen": 1705420800000
    }
  ]
}
```

### XOR Distance Calculation

```
distance(A, B) = A XOR B

A = 1010110...
B = 1100010...
    ─────────
D = 0110100...
```

K-bucket index = position of highest bit in distance

---

## NAT Traversal

### STUN-like Protocol

```
┌────────┐         ┌────────┐         ┌────────┐
│ Node A │         │  Relay │         │ Node B │
│  (NAT) │         │ Server │         │  (NAT) │
└───┬────┘         └───┬────┘         └───┬────┘
    │                  │                  │
    │  1. BINDING_REQUEST                 │
    │  ──────────────► │                  │
    │                  │                  │
    │  2. BINDING_RESPONSE                │
    │  ◄────────────── │                  │
    │  (reflexive_addr)│                  │
    │                  │                  │
    │  3. Exchange reflexive addresses    │
    │  ◄═══════════════════════════════► │
    │      (via common peer or server)    │
    │                  │                  │
    │  4. PUNCH_REQUEST (to B's addr)     │
    │  ──────────────────────────────────►│
    │                  │                  │
    │  5. PUNCH_REQUEST (to A's addr)     │
    │◄────────────────────────────────────│
    │                  │                  │
    │  ═══════════════════════════════════│
    │        Direct P2P Connection        │
    │  ═══════════════════════════════════│
```

### NAT Messages

| Type | Code | Description |
|------|------|-------------|
| BINDING_REQUEST | 0x70 | Request external address |
| BINDING_RESPONSE | 0x71 | Return external address |
| PUNCH_REQUEST | 0x72 | NAT punch attempt |
| PUNCH_RESPONSE | 0x73 | Punch acknowledgment |
| RELAY_REQUEST | 0x74 | Request relay (fallback) |

#### BINDING_REQUEST

```json
{
  "type": "binding_request",
  "transaction_id": "uuid"
}
```

#### BINDING_RESPONSE

```json
{
  "type": "binding_response",
  "transaction_id": "uuid",
  "reflexive_address": "203.0.113.1",
  "reflexive_port": 54321,
  "local_address": "192.168.1.100",
  "local_port": 45678
}
```

---

## Security

### Cryptographic Primitives

| Purpose | Algorithm |
|---------|-----------|
| Key Exchange | X25519 (ECDH) |
| Signing | Ed25519 |
| Encryption | AES-256-GCM |
| Hashing | SHA-256, RIPEMD-160 |
| KDF | HKDF-SHA256 |

### Key Derivation

```
Master Secret = ECDH(private_key_A, public_key_B)

Encryption Key = HKDF(master_secret, salt, "spiderx-encrypt", 32)
MAC Key = HKDF(master_secret, salt, "spiderx-mac", 32)
```

### Message Authentication

```
1. Serialize message to JSON
2. Compute signature = Ed25519.Sign(private_key, message_bytes)
3. Encrypt: ciphertext = AES-GCM.Encrypt(encryption_key, nonce, message_bytes)
4. Send: sender_id || public_key || nonce || ciphertext || auth_tag || signature
```

### Replay Protection

- Every message includes timestamp
- Receiver rejects messages older than 5 minutes
- Nonces must be unique per sender
- Sequence numbers for ordered protocols

### DDoS Mitigation

```
Rate Limits:
- Max 100 messages/second per peer
- Max 10 new connections/second
- Max 50 concurrent connections

Challenge-Response for new connections:
1. Receive connection request
2. Send challenge (random bytes)
3. Require proof-of-work or signed response
4. Accept connection
```

---

## Error Codes

| Code | Name | Description |
|------|------|-------------|
| 0x00 | SUCCESS | Operation successful |
| 0x01 | INVALID_VERSION | Unsupported protocol version |
| 0x02 | INVALID_SIGNATURE | Signature verification failed |
| 0x03 | DECRYPTION_FAILED | Unable to decrypt message |
| 0x04 | TIMEOUT | Operation timed out |
| 0x05 | NOT_AUTHORIZED | Permission denied |
| 0x06 | PEER_NOT_FOUND | Target peer not found |
| 0x07 | RATE_LIMITED | Too many requests |
| 0x08 | TRANSFER_FAILED | File transfer error |
| 0x09 | CALL_FAILED | Voice call error |
| 0x0A | INVALID_MESSAGE | Malformed message |

### ERROR Message

```json
{
  "type": "error",
  "code": 5,
  "message": "Permission denied",
  "reference_id": "uuid of original message"
}
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2024-01 | Initial specification |

---

## Appendix A: Constants

```
MAGIC_NUMBER = 0x53505858 ("SPXX")
PROTOCOL_VERSION = 1
MAX_PACKET_SIZE = 65535
DEFAULT_UDP_PORT = 45678
DEFAULT_TCP_PORT = 45679
KEEPALIVE_INTERVAL = 30 seconds
CONNECTION_TIMEOUT = 10 seconds
MESSAGE_TIMEOUT = 5 minutes
MAX_PEERS = 50
K_BUCKET_SIZE = 20
CHUNK_SIZE = 65536 (64 KB)
VOICE_FRAME_SIZE = 960 samples (20ms at 48kHz)
```
