using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SpiderX.Core.Models;

/// <summary>
/// Represents a file shared on the P2P network
/// </summary>
public class SharedFile
{
    /// <summary>
    /// Gets or sets the unique identifier for this shared file (SHA256 hash of content).
    /// </summary>
    public required string FileHash { get; set; }

    /// <summary>
    /// Gets or sets the display name of the file.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the file extension (e.g., ".mp4", ".zip").
    /// </summary>
    public required string Extension { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes.
    /// </summary>
    public required long Size { get; set; }

    /// <summary>
    /// Gets or sets the description of the file.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the category of the file.
    /// </summary>
    public FileCategory Category { get; set; } = FileCategory.Other;

    /// <summary>
    /// Gets or sets the tags for searching.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the Base64-encoded thumbnail image (for images/videos).
    /// </summary>
    public string? ThumbnailBase64 { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the thumbnail.
    /// </summary>
    public string? ThumbnailMimeType { get; set; }

    /// <summary>
    /// Gets or sets the size of each chunk in bytes (default 256KB).
    /// </summary>
    public int ChunkSize { get; set; } = 256 * 1024;

    /// <summary>
    /// Gets or sets the total number of chunks.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Gets or sets the hash of each chunk for verification.
    /// </summary>
    public List<string> ChunkHashes { get; set; } = [];

    /// <summary>
    /// Gets or sets when the file was shared.
    /// </summary>
    public DateTime SharedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the owner's peer ID who originally shared this file.
    /// </summary>
    public required string OwnerPeerId { get; set; }

    /// <summary>
    /// Gets or sets the owner's display name.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Gets or sets the local file path (only set if we have the file locally).
    /// </summary>
    [JsonIgnore]
    public string? LocalPath { get; set; }

    /// <summary>
    /// Gets the formatted file size for display.
    /// </summary>
    [JsonIgnore]
    public string SizeDisplay => FormatBytes(Size);

    /// <summary>
    /// Gets the category display name.
    /// </summary>
    [JsonIgnore]
    public string CategoryDisplay => Category.ToString();

    /// <summary>
    /// Gets the icon based on file type.
    /// </summary>
    [JsonIgnore]
    public string Icon => GetIconForCategory();

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private string GetIconForCategory()
    {
        return Category switch
        {
            FileCategory.Video => "🎬",
            FileCategory.Audio => "🎵",
            FileCategory.Image => "🖼️",
            FileCategory.Document => "📄",
            FileCategory.Archive => "📦",
            FileCategory.Software => "💿",
            FileCategory.Game => "🎮",
            FileCategory.Ebook => "📚",
            _ => "📁"
        };
    }

    /// <summary>
    /// Create a SharedFile from a local file path
    /// </summary>
    public static async Task<SharedFile> FromFileAsync(string filePath, string ownerPeerId, string? ownerName = null, int chunkSize = 256 * 1024)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File not found", filePath);

        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / chunkSize);
        var chunkHashes = new List<string>(totalChunks);

        // Calculate file hash and chunk hashes
        string fileHash;
        using (var sha256 = SHA256.Create())
        using (var fileStream = File.OpenRead(filePath))
        {
            // Calculate overall file hash
            fileHash = Convert.ToHexString(await sha256.ComputeHashAsync(fileStream)).ToLowerInvariant();

            // Reset stream and calculate chunk hashes
            fileStream.Position = 0;
            var buffer = new byte[chunkSize];

            for (int i = 0; i < totalChunks; i++)
            {
                var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, chunkSize));
                var chunkHash = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, bytesRead))).ToLowerInvariant();
                chunkHashes.Add(chunkHash);
            }
        }

        var extension = fileInfo.Extension.ToLowerInvariant();
        var category = GetCategoryFromExtension(extension);

        return new SharedFile
        {
            FileHash = fileHash,
            Name = fileInfo.Name,
            Extension = extension,
            Size = fileInfo.Length,
            Category = category,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            ChunkHashes = chunkHashes,
            OwnerPeerId = ownerPeerId,
            OwnerName = ownerName,
            LocalPath = filePath
        };
    }

    private static FileCategory GetCategoryFromExtension(string extension)
    {
        return extension switch
        {
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" or ".webm" => FileCategory.Video,
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma" or ".m4a" => FileCategory.Audio,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => FileCategory.Image,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" => FileCategory.Document,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => FileCategory.Archive,
            ".exe" or ".msi" or ".dmg" or ".app" or ".deb" or ".rpm" => FileCategory.Software,
            ".iso" or ".bin" or ".cue" => FileCategory.Game,
            ".epub" or ".mobi" or ".azw" or ".azw3" => FileCategory.Ebook,
            _ => FileCategory.Other
        };
    }
}

/// <summary>
/// File categories for organization
/// </summary>
public enum FileCategory
{
    Other = 0,
    Video = 1,
    Audio = 2,
    Image = 3,
    Document = 4,
    Archive = 5,
    Software = 6,
    Game = 7,
    Ebook = 8
}

/// <summary>
/// Represents a catalog of files shared by a peer
/// </summary>
public class SharedCatalog
{
    /// <summary>
    /// Gets or sets the peer ID of the catalog owner.
    /// </summary>
    public required string PeerId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the catalog owner.
    /// </summary>
    public string? PeerName { get; set; }

    /// <summary>
    /// Gets or sets the list of shared files.
    /// </summary>
    public List<SharedFile> Files { get; set; } = [];

    /// <summary>
    /// Gets or sets when this catalog was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the total size of all files.
    /// </summary>
    [JsonIgnore]
    public long TotalSize => Files.Sum(f => f.Size);

    /// <summary>
    /// Gets the formatted total size.
    /// </summary>
    [JsonIgnore]
    public string TotalSizeDisplay => FormatBytes(TotalSize);

    /// <summary>
    /// Gets the number of files.
    /// </summary>
    [JsonIgnore]
    public int FileCount => Files.Count;

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Represents an active download
/// </summary>
public class FileDownload
{
    /// <summary>
    /// Gets or sets the file being downloaded.
    /// </summary>
    public required SharedFile File { get; set; }

    /// <summary>
    /// Gets or sets the download state.
    /// </summary>
    public DownloadState State { get; set; } = DownloadState.Pending;

    /// <summary>
    /// Gets or sets the peers we're downloading from.
    /// </summary>
    public List<string> SourcePeers { get; set; } = [];

    /// <summary>
    /// Gets or sets which chunks have been downloaded.
    /// </summary>
    public bool[] ChunksCompleted { get; set; } = [];

    /// <summary>
    /// Gets or sets which chunks are currently being downloaded.
    /// </summary>
    public bool[] ChunksInProgress { get; set; } = [];

    /// <summary>
    /// Gets or sets the bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the download speed in bytes per second.
    /// </summary>
    public double SpeedBytesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets when download started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the local path where the file is being saved.
    /// </summary>
    public required string DestinationPath { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public double Progress => File.Size > 0 ? (double)BytesDownloaded / File.Size * 100 : 0;

    /// <summary>
    /// Gets the formatted progress.
    /// </summary>
    public string ProgressDisplay => $"{Progress:0.#}%";

    /// <summary>
    /// Gets the formatted speed.
    /// </summary>
    public string SpeedDisplay => FormatSpeed(SpeedBytesPerSecond);

    /// <summary>
    /// Gets the estimated time remaining.
    /// </summary>
    public string EtaDisplay => CalculateEta();

    /// <summary>
    /// Gets the number of completed chunks.
    /// </summary>
    public int CompletedChunks => ChunksCompleted.Count(c => c);

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:0.#} KB/s";
        return $"{bytesPerSecond / 1024 / 1024:0.##} MB/s";
    }

    private string CalculateEta()
    {
        if (SpeedBytesPerSecond <= 0) return "Calculating...";

        var remainingBytes = File.Size - BytesDownloaded;
        var seconds = remainingBytes / SpeedBytesPerSecond;

        if (seconds < 60) return $"{seconds:0}s";
        if (seconds < 3600) return $"{seconds / 60:0}m {seconds % 60:0}s";
        return $"{seconds / 3600:0}h {(seconds % 3600) / 60:0}m";
    }
}

/// <summary>
/// Download state
/// </summary>
public enum DownloadState
{
    Pending,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}
