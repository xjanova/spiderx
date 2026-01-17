using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpiderX.Core;
using SpiderX.Core.Messages;
using SpiderX.Crypto;

namespace SpiderX.Services;

/// <summary>
/// Service for peer-to-peer file transfers
/// </summary>
public class FileTransferService : IDisposable
{
    private readonly SpiderNode _node;
    private readonly ConcurrentDictionary<string, FileTransfer> _transfers = new();
    private readonly string _downloadPath;
    private readonly int _chunkSize;
    private bool _disposed;

    /// <summary>
    /// Event raised when a file offer is received
    /// </summary>
    public event EventHandler<FileOfferEventArgs>? FileOfferReceived;

    /// <summary>
    /// Event raised when transfer progress updates
    /// </summary>
    public event EventHandler<FileProgressEventArgs>? TransferProgress;

    /// <summary>
    /// Event raised when a transfer completes
    /// </summary>
    public event EventHandler<FileCompletedEventArgs>? TransferCompleted;

    /// <summary>
    /// Event raised when a transfer fails
    /// </summary>
    public event EventHandler<FileFailedEventArgs>? TransferFailed;

    /// <summary>
    /// Active transfers
    /// </summary>
    public IReadOnlyCollection<FileTransfer> ActiveTransfers => _transfers.Values.ToList();

    public FileTransferService(SpiderNode node, string downloadPath, int chunkSize = 64 * 1024)
    {
        _node = node;
        _downloadPath = downloadPath;
        _chunkSize = chunkSize;

        Directory.CreateDirectory(_downloadPath);

        _node.Peers.DataReceived += OnDataReceived;
    }

    /// <summary>
    /// Offers a file to a peer
    /// </summary>
    public async Task<FileTransfer> OfferFileAsync(SpiderId recipientId, string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        var fileHash = await ComputeFileHashAsync(filePath);
        var fileId = Guid.NewGuid().ToString("N");

        var transfer = new FileTransfer
        {
            Id = fileId,
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            FileHash = fileHash,
            ChunkSize = _chunkSize,
            TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / _chunkSize),
            PeerId = recipientId,
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Pending
        };

        _transfers[fileId] = transfer;

        var offer = new FileOfferMessage
        {
            FileId = fileId,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            FileHash = fileHash,
            ChunkSize = _chunkSize,
            RecipientId = recipientId.Address
        };

        await _node.Peers.SendAsync(recipientId, offer);
        return transfer;
    }

    /// <summary>
    /// Accepts a file offer
    /// </summary>
    public async Task AcceptFileAsync(string fileId)
    {
        if (!_transfers.TryGetValue(fileId, out var transfer))
            throw new InvalidOperationException("Transfer not found");

        transfer.Status = TransferStatus.InProgress;
        transfer.FilePath = Path.Combine(_downloadPath, transfer.FileName);

        // Initialize file
        await using var fs = File.Create(transfer.FilePath);
        fs.SetLength(transfer.FileSize);

        // Request first chunk
        var request = new FileRequestMessage
        {
            FileId = fileId,
            ChunkIndex = 0,
            Accepted = true
        };

        await _node.Peers.SendAsync(transfer.PeerId, request);
    }

    /// <summary>
    /// Rejects a file offer
    /// </summary>
    public async Task RejectFileAsync(string fileId)
    {
        if (!_transfers.TryGetValue(fileId, out var transfer))
            return;

        var request = new FileRequestMessage
        {
            FileId = fileId,
            ChunkIndex = 0,
            Accepted = false
        };

        await _node.Peers.SendAsync(transfer.PeerId, request);
        _transfers.TryRemove(fileId, out _);
    }

    /// <summary>
    /// Cancels an active transfer
    /// </summary>
    public void CancelTransfer(string fileId)
    {
        if (_transfers.TryRemove(fileId, out var transfer))
        {
            transfer.Status = TransferStatus.Cancelled;
            TransferFailed?.Invoke(this, new FileFailedEventArgs
            {
                Transfer = transfer,
                Error = "Cancelled by user"
            });
        }
    }

    private void OnDataReceived(object? sender, PeerDataEventArgs e)
    {
        switch (e.Message)
        {
            case FileOfferMessage offer:
                HandleFileOffer(e.Peer.Id, offer);
                break;

            case FileRequestMessage request:
                _ = HandleFileRequestAsync(e.Peer.Id, request);
                break;

            case FileChunkMessage chunk:
                _ = HandleFileChunkAsync(e.Peer.Id, chunk);
                break;
        }
    }

    private void HandleFileOffer(SpiderId senderId, FileOfferMessage offer)
    {
        var transfer = new FileTransfer
        {
            Id = offer.FileId,
            FileName = offer.FileName,
            FileSize = offer.FileSize,
            FileHash = offer.FileHash,
            ChunkSize = offer.ChunkSize,
            TotalChunks = (int)Math.Ceiling((double)offer.FileSize / offer.ChunkSize),
            PeerId = senderId,
            Direction = TransferDirection.Download,
            Status = TransferStatus.Pending
        };

        _transfers[offer.FileId] = transfer;

        FileOfferReceived?.Invoke(this, new FileOfferEventArgs { Transfer = transfer });
    }

    private async Task HandleFileRequestAsync(SpiderId requesterId, FileRequestMessage request)
    {
        if (!_transfers.TryGetValue(request.FileId, out var transfer))
            return;

        if (!request.Accepted)
        {
            transfer.Status = TransferStatus.Rejected;
            _transfers.TryRemove(request.FileId, out _);
            TransferFailed?.Invoke(this, new FileFailedEventArgs
            {
                Transfer = transfer,
                Error = "Rejected by recipient"
            });
            return;
        }

        transfer.Status = TransferStatus.InProgress;

        // Send requested chunk
        await SendChunkAsync(transfer, request.ChunkIndex);
    }

    private async Task SendChunkAsync(FileTransfer transfer, int chunkIndex)
    {
        if (string.IsNullOrEmpty(transfer.FilePath))
            return;

        try
        {
            await using var fs = File.OpenRead(transfer.FilePath);
            fs.Seek((long)chunkIndex * transfer.ChunkSize, SeekOrigin.Begin);

            var buffer = new byte[Math.Min(transfer.ChunkSize, transfer.FileSize - (long)chunkIndex * transfer.ChunkSize)];
            var bytesRead = await fs.ReadAsync(buffer);

            if (bytesRead < buffer.Length)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            var chunk = new FileChunkMessage
            {
                FileId = transfer.Id,
                ChunkIndex = chunkIndex,
                TotalChunks = transfer.TotalChunks,
                Data = buffer,
                Hash = Convert.ToHexString(SHA256.HashData(buffer))
            };

            await _node.Peers.SendAsync(transfer.PeerId, chunk);

            transfer.TransferredChunks = chunkIndex + 1;
            transfer.TransferredBytes += buffer.Length;

            TransferProgress?.Invoke(this, new FileProgressEventArgs
            {
                Transfer = transfer,
                Progress = (double)transfer.TransferredChunks / transfer.TotalChunks
            });
        }
        catch (Exception ex)
        {
            transfer.Status = TransferStatus.Failed;
            TransferFailed?.Invoke(this, new FileFailedEventArgs
            {
                Transfer = transfer,
                Error = ex.Message
            });
        }
    }

    private async Task HandleFileChunkAsync(SpiderId senderId, FileChunkMessage chunk)
    {
        if (!_transfers.TryGetValue(chunk.FileId, out var transfer))
            return;

        if (string.IsNullOrEmpty(transfer.FilePath))
            return;

        try
        {
            // Verify chunk hash
            var expectedHash = Convert.ToHexString(SHA256.HashData(chunk.Data));
            if (expectedHash != chunk.Hash)
            {
                // Request chunk again
                var retryRequest = new FileRequestMessage
                {
                    FileId = chunk.FileId,
                    ChunkIndex = chunk.ChunkIndex,
                    Accepted = true
                };
                await _node.Peers.SendAsync(transfer.PeerId, retryRequest);
                return;
            }

            // Write chunk to file
            await using var fs = File.OpenWrite(transfer.FilePath);
            fs.Seek((long)chunk.ChunkIndex * transfer.ChunkSize, SeekOrigin.Begin);
            await fs.WriteAsync(chunk.Data);

            transfer.TransferredChunks = chunk.ChunkIndex + 1;
            transfer.TransferredBytes += chunk.Data.Length;
            transfer.ReceivedChunks.Add(chunk.ChunkIndex);

            TransferProgress?.Invoke(this, new FileProgressEventArgs
            {
                Transfer = transfer,
                Progress = (double)transfer.TransferredChunks / transfer.TotalChunks
            });

            // Request next chunk or complete
            if (chunk.ChunkIndex + 1 < chunk.TotalChunks)
            {
                var nextRequest = new FileRequestMessage
                {
                    FileId = chunk.FileId,
                    ChunkIndex = chunk.ChunkIndex + 1,
                    Accepted = true
                };
                await _node.Peers.SendAsync(transfer.PeerId, nextRequest);
            }
            else
            {
                // Verify file hash
                var fileHash = await ComputeFileHashAsync(transfer.FilePath);
                if (fileHash == transfer.FileHash)
                {
                    transfer.Status = TransferStatus.Completed;
                    TransferCompleted?.Invoke(this, new FileCompletedEventArgs { Transfer = transfer });
                }
                else
                {
                    transfer.Status = TransferStatus.Failed;
                    TransferFailed?.Invoke(this, new FileFailedEventArgs
                    {
                        Transfer = transfer,
                        Error = "File hash mismatch"
                    });
                }

                _transfers.TryRemove(chunk.FileId, out _);
            }
        }
        catch (Exception ex)
        {
            transfer.Status = TransferStatus.Failed;
            TransferFailed?.Invoke(this, new FileFailedEventArgs
            {
                Transfer = transfer,
                Error = ex.Message
            });
        }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var fs = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(fs);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _node.Peers.DataReceived -= OnDataReceived;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents an active file transfer
/// </summary>
public class FileTransfer
{
    /// <summary>
    /// Gets the unique transfer identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or sets the local file path.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// Gets the file hash.
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// Gets the chunk size in bytes.
    /// </summary>
    public required int ChunkSize { get; init; }

    /// <summary>
    /// Gets the total number of chunks.
    /// </summary>
    public required int TotalChunks { get; init; }

    /// <summary>
    /// Gets the peer ID.
    /// </summary>
    public required SpiderId PeerId { get; init; }

    /// <summary>
    /// Gets the transfer direction.
    /// </summary>
    public required TransferDirection Direction { get; init; }

    /// <summary>
    /// Gets or sets the transfer status.
    /// </summary>
    public TransferStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of transferred chunks.
    /// </summary>
    public int TransferredChunks { get; set; }

    /// <summary>
    /// Gets or sets the number of transferred bytes.
    /// </summary>
    public long TransferredBytes { get; set; }

    /// <summary>
    /// Gets the set of received chunk indices.
    /// </summary>
    public HashSet<int> ReceivedChunks { get; } = [];

    /// <summary>
    /// Gets the transfer progress (0-1).
    /// </summary>
    public double Progress => TotalChunks > 0 ? (double)TransferredChunks / TotalChunks : 0;
}

/// <summary>
/// Transfer direction
/// </summary>
public enum TransferDirection
{
    Upload,
    Download
}

/// <summary>
/// Transfer status
/// </summary>
public enum TransferStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    Rejected
}

/// <summary>
/// Event args for file offer
/// </summary>
public class FileOfferEventArgs : EventArgs
{
    /// <summary>
    /// Gets the file transfer.
    /// </summary>
    public required FileTransfer Transfer { get; init; }
}

/// <summary>
/// Event args for transfer progress
/// </summary>
public class FileProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the file transfer.
    /// </summary>
    public required FileTransfer Transfer { get; init; }

    /// <summary>
    /// Gets the progress value (0-1).
    /// </summary>
    public required double Progress { get; init; }
}

/// <summary>
/// Event args for completed transfer
/// </summary>
public class FileCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the completed file transfer.
    /// </summary>
    public required FileTransfer Transfer { get; init; }
}

/// <summary>
/// Event args for failed transfer
/// </summary>
public class FileFailedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the failed file transfer.
    /// </summary>
    public required FileTransfer Transfer { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Error { get; init; }
}
