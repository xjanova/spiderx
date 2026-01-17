using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpiderX.Core.Messages;

/// <summary>
/// Base class for all protocol messages
/// </summary>
[JsonDerivedType(typeof(HandshakeMessage), "handshake")]
[JsonDerivedType(typeof(HandshakeAckMessage), "handshake_ack")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
[JsonDerivedType(typeof(FindNodeMessage), "find_node")]
[JsonDerivedType(typeof(FindNodeResponseMessage), "find_node_response")]
[JsonDerivedType(typeof(ChatMessage), "chat")]
[JsonDerivedType(typeof(FileOfferMessage), "file_offer")]
[JsonDerivedType(typeof(FileRequestMessage), "file_request")]
[JsonDerivedType(typeof(FileChunkMessage), "file_chunk")]
[JsonDerivedType(typeof(VoiceDataMessage), "voice_data")]
[JsonDerivedType(typeof(PermissionRequestMessage), "permission_request")]
[JsonDerivedType(typeof(PermissionResponseMessage), "permission_response")]
[JsonDerivedType(typeof(VirtualLanAnnounceMessage), "vlan_announce")]
[JsonDerivedType(typeof(VirtualLanPacketMessage), "vlan_packet")]
[JsonDerivedType(typeof(CatalogRequestMessage), "catalog_request")]
[JsonDerivedType(typeof(CatalogResponseMessage), "catalog_response")]
[JsonDerivedType(typeof(P2PChunkRequestMessage), "p2p_chunk_request")]
[JsonDerivedType(typeof(P2PChunkResponseMessage), "p2p_chunk_response")]
[JsonDerivedType(typeof(FileAvailabilityMessage), "file_availability")]
public abstract class Message
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("sender")]
    public string? SenderId { get; set; }

    public byte[] Serialize()
    {
        var json = JsonSerializer.Serialize<Message>(this, JsonOptions.Default);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public static Message? Deserialize(byte[] data)
    {
        var json = System.Text.Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<Message>(json, JsonOptions.Default);
    }
}

/// <summary>
/// Handshake message to establish connection and exchange identities
/// </summary>
public class HandshakeMessage : Message
{
    public override string Type => "handshake";

    [JsonPropertyName("public_key")]
    public required byte[] PublicKey { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = ["chat", "file", "voice"];
}

/// <summary>
/// Handshake acknowledgment
/// </summary>
public class HandshakeAckMessage : Message
{
    public override string Type => "handshake_ack";

    [JsonPropertyName("public_key")]
    public required byte[] PublicKey { get; set; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; } = true;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Ping message for keepalive
/// </summary>
public class PingMessage : Message
{
    public override string Type => "ping";
}

/// <summary>
/// Pong response to ping
/// </summary>
public class PongMessage : Message
{
    public override string Type => "pong";

    [JsonPropertyName("ping_id")]
    public required string PingId { get; set; }
}

/// <summary>
/// DHT find node request
/// </summary>
public class FindNodeMessage : Message
{
    public override string Type => "find_node";

    [JsonPropertyName("target_id")]
    public required string TargetId { get; set; }
}

/// <summary>
/// DHT find node response
/// </summary>
public class FindNodeResponseMessage : Message
{
    public override string Type => "find_node_response";

    [JsonPropertyName("request_id")]
    public required string RequestId { get; set; }

    [JsonPropertyName("nodes")]
    public List<NodeInfo> Nodes { get; set; } = [];
}

/// <summary>
/// Node information for DHT
/// </summary>
public class NodeInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("address")]
    public required string Address { get; set; }

    [JsonPropertyName("port")]
    public required int Port { get; set; }
}

/// <summary>
/// Chat message
/// </summary>
public class ChatMessage : Message
{
    public override string Type => "chat";

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("recipient")]
    public required string RecipientId { get; set; }

    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; set; }
}

/// <summary>
/// File transfer offer
/// </summary>
public class FileOfferMessage : Message
{
    public override string Type => "file_offer";

    [JsonPropertyName("file_id")]
    public required string FileId { get; set; }

    [JsonPropertyName("file_name")]
    public required string FileName { get; set; }

    [JsonPropertyName("file_size")]
    public required long FileSize { get; set; }

    [JsonPropertyName("file_hash")]
    public required string FileHash { get; set; }

    [JsonPropertyName("chunk_size")]
    public int ChunkSize { get; set; } = 64 * 1024; // 64KB

    [JsonPropertyName("recipient")]
    public required string RecipientId { get; set; }
}

/// <summary>
/// File chunk request
/// </summary>
public class FileRequestMessage : Message
{
    public override string Type => "file_request";

    [JsonPropertyName("file_id")]
    public required string FileId { get; set; }

    [JsonPropertyName("chunk_index")]
    public required int ChunkIndex { get; set; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; } = true;
}

/// <summary>
/// File chunk data
/// </summary>
public class FileChunkMessage : Message
{
    public override string Type => "file_chunk";

    [JsonPropertyName("file_id")]
    public required string FileId { get; set; }

    [JsonPropertyName("chunk_index")]
    public required int ChunkIndex { get; set; }

    [JsonPropertyName("total_chunks")]
    public required int TotalChunks { get; set; }

    [JsonPropertyName("data")]
    public required byte[] Data { get; set; }

    [JsonPropertyName("hash")]
    public required string Hash { get; set; }
}

/// <summary>
/// Voice/audio data
/// </summary>
public class VoiceDataMessage : Message
{
    public override string Type => "voice_data";

    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }

    [JsonPropertyName("sequence")]
    public required int Sequence { get; set; }

    [JsonPropertyName("data")]
    public required byte[] Data { get; set; }

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = "opus";
}

/// <summary>
/// Permission request (for contacts, calls, etc.)
/// </summary>
public class PermissionRequestMessage : Message
{
    public override string Type => "permission_request";

    [JsonPropertyName("permission_type")]
    public required string PermissionType { get; set; } // "contact", "call", "file"

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("message")]
    public string? CustomMessage { get; set; }
}

/// <summary>
/// Permission response
/// </summary>
public class PermissionResponseMessage : Message
{
    public override string Type => "permission_response";

    [JsonPropertyName("request_id")]
    public required string RequestId { get; set; }

    [JsonPropertyName("granted")]
    public required bool Granted { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }
}

/// <summary>
/// Virtual LAN announcement message
/// </summary>
public class VirtualLanAnnounceMessage : Message
{
    public override string Type => "vlan_announce";

    [JsonPropertyName("virtual_ip")]
    public required string VirtualIp { get; set; }

    [JsonPropertyName("is_joining")]
    public bool IsJoining { get; set; }

    [JsonPropertyName("hostname")]
    public required string Hostname { get; set; }

    [JsonPropertyName("capabilities")]
    public VlanCapabilities Capabilities { get; set; }
}

/// <summary>
/// Virtual LAN packet message for tunneling network traffic
/// </summary>
public class VirtualLanPacketMessage : Message
{
    public override string Type => "vlan_packet";

    [JsonPropertyName("source_ip")]
    public required string SourceIp { get; set; }

    [JsonPropertyName("destination_ip")]
    public required string DestinationIp { get; set; }

    [JsonPropertyName("packet_data")]
    public required byte[] PacketData { get; set; }

    [JsonPropertyName("packet_type")]
    public VlanPacketType PacketType { get; set; }

    [JsonPropertyName("source_port")]
    public int SourcePort { get; set; }

    [JsonPropertyName("destination_port")]
    public int DestinationPort { get; set; }
}

/// <summary>
/// VLAN capabilities flags
/// </summary>
[Flags]
public enum VlanCapabilities
{
    None = 0,
    BroadcastRelay = 1,
    GameDiscovery = 2,
    TapAdapter = 4,
    FullTunnel = 8
}

/// <summary>
/// VLAN packet types
/// </summary>
public enum VlanPacketType
{
    Unicast,
    Broadcast,
    BroadcastRelay,
    Multicast
}

// ============================================
// P2P File Sharing Messages
// ============================================

/// <summary>
/// Request a peer's file catalog
/// </summary>
public class CatalogRequestMessage : Message
{
    public override string Type => "catalog_request";

    [JsonPropertyName("category_filter")]
    public int? CategoryFilter { get; set; }

    [JsonPropertyName("search_query")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 0;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Response with the peer's file catalog
/// </summary>
public class CatalogResponseMessage : Message
{
    public override string Type => "catalog_response";

    [JsonPropertyName("request_id")]
    public required string RequestId { get; set; }

    [JsonPropertyName("peer_name")]
    public string? PeerName { get; set; }

    [JsonPropertyName("files")]
    public List<SharedFileInfo> Files { get; set; } = [];

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }
}

/// <summary>
/// Minimal file info for catalog transfer
/// </summary>
public class SharedFileInfo
{
    [JsonPropertyName("hash")]
    public required string FileHash { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("ext")]
    public required string Extension { get; set; }

    [JsonPropertyName("size")]
    public required long Size { get; set; }

    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    [JsonPropertyName("cat")]
    public int Category { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("thumb")]
    public string? ThumbnailBase64 { get; set; }

    [JsonPropertyName("thumb_mime")]
    public string? ThumbnailMimeType { get; set; }

    [JsonPropertyName("chunk_size")]
    public int ChunkSize { get; set; }

    [JsonPropertyName("chunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("chunk_hashes")]
    public List<string> ChunkHashes { get; set; } = [];

    [JsonPropertyName("shared_at")]
    public long SharedAt { get; set; }
}

/// <summary>
/// Request a specific chunk from a peer (for multi-peer downloads)
/// </summary>
public class P2PChunkRequestMessage : Message
{
    public override string Type => "p2p_chunk_request";

    [JsonPropertyName("file_hash")]
    public required string FileHash { get; set; }

    [JsonPropertyName("chunk_indices")]
    public required List<int> ChunkIndices { get; set; }
}

/// <summary>
/// Response with chunk data
/// </summary>
public class P2PChunkResponseMessage : Message
{
    public override string Type => "p2p_chunk_response";

    [JsonPropertyName("request_id")]
    public required string RequestId { get; set; }

    [JsonPropertyName("file_hash")]
    public required string FileHash { get; set; }

    [JsonPropertyName("chunk_index")]
    public required int ChunkIndex { get; set; }

    [JsonPropertyName("data")]
    public required byte[] Data { get; set; }

    [JsonPropertyName("hash")]
    public required string ChunkHash { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

/// <summary>
/// Announce which chunks of a file we have (for swarming)
/// </summary>
public class FileAvailabilityMessage : Message
{
    public override string Type => "file_availability";

    [JsonPropertyName("file_hash")]
    public required string FileHash { get; set; }

    [JsonPropertyName("available_chunks")]
    public required List<int> AvailableChunks { get; set; }

    [JsonPropertyName("total_chunks")]
    public required int TotalChunks { get; set; }

    [JsonPropertyName("is_seeder")]
    public bool IsSeeder { get; set; }
}

/// <summary>
/// JSON serialization options
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
