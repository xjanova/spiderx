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
