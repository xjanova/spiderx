using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpiderX.Core;
using SpiderX.Core.Messages;
using SpiderX.Core.Models;
using SpiderX.Crypto;

namespace SpiderX.Services;

/// <summary>
/// P2P file sharing service with BitTorrent-like multi-peer downloading
/// </summary>
public class P2PFileSharingService : IDisposable
{
    private readonly SpiderNode _node;
    private readonly ConcurrentDictionary<string, SharedFile> _sharedFiles = new();
    private readonly ConcurrentDictionary<string, SharedCatalog> _peerCatalogs = new();
    private readonly ConcurrentDictionary<string, FileDownload> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _fileProviders = new(); // fileHash -> set of peerIds
    private readonly string _sharedFolder;
    private readonly string _downloadFolder;
    private readonly SemaphoreSlim _downloadSemaphore = new(5); // Max 5 concurrent chunk downloads
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public event EventHandler<SharedFile>? FileShared;
    public event EventHandler<SharedFile>? FileUnshared;
    public event EventHandler<FileDownload>? DownloadStarted;
    public event EventHandler<FileDownload>? DownloadProgress;
    public event EventHandler<FileDownload>? DownloadCompleted;
    public event EventHandler<FileDownload>? DownloadFailed;
    public event EventHandler<SharedCatalog>? CatalogReceived;

    public IReadOnlyDictionary<string, SharedFile> SharedFiles => _sharedFiles;
    public IReadOnlyDictionary<string, SharedCatalog> PeerCatalogs => _peerCatalogs;
    public IReadOnlyDictionary<string, FileDownload> ActiveDownloads => _activeDownloads;
    public bool IsRunning => _isRunning;

    public P2PFileSharingService(SpiderNode node, string? sharedFolder = null, string? downloadFolder = null)
    {
        _node = node;
        _sharedFolder = sharedFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SpiderX", "Shared");
        _downloadFolder = downloadFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SpiderX", "Downloads");

        Directory.CreateDirectory(_sharedFolder);
        Directory.CreateDirectory(_downloadFolder);
    }

    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _cts = new CancellationTokenSource();
        _isRunning = true;

        // Subscribe to messages
        _node.MessageReceived += OnMessageReceived;

        // Load previously shared files
        await LoadSharedFilesAsync();
    }

    public Task StopAsync()
    {
        if (!_isRunning)
            return Task.CompletedTask;

        _cts?.Cancel();
        _isRunning = false;
        _node.MessageReceived -= OnMessageReceived;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Share a file on the network
    /// </summary>
    public async Task<SharedFile> ShareFileAsync(string filePath, string? description = null, FileCategory? category = null, List<string>? tags = null)
    {
        var ownerId = _node.LocalId.ToString();
        var sharedFile = await SharedFile.FromFileAsync(filePath, ownerId);

        sharedFile.Description = description;
        if (category.HasValue)
            sharedFile.Category = category.Value;
        if (tags != null)
            sharedFile.Tags = tags;

        // Generate thumbnail for images/videos
        await GenerateThumbnailAsync(sharedFile, filePath);

        _sharedFiles[sharedFile.FileHash] = sharedFile;

        // Save metadata
        await SaveSharedFileMetadataAsync(sharedFile);

        FileShared?.Invoke(this, sharedFile);

        return sharedFile;
    }

    /// <summary>
    /// Share an entire folder
    /// </summary>
    public async Task<List<SharedFile>> ShareFolderAsync(string folderPath, bool recursive = true, FileCategory? category = null)
    {
        var files = new List<SharedFile>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var filePath in Directory.GetFiles(folderPath, "*.*", searchOption))
        {
            try
            {
                var sharedFile = await ShareFileAsync(filePath, category: category);
                files.Add(sharedFile);
            }
            catch (Exception)
            {
                // Skip files that can't be shared
            }
        }

        return files;
    }

    /// <summary>
    /// Unshare a file
    /// </summary>
    public void UnshareFile(string fileHash)
    {
        if (_sharedFiles.TryRemove(fileHash, out var file))
        {
            // Remove metadata file
            var metadataPath = Path.Combine(_sharedFolder, $"{fileHash}.meta.json");
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);

            FileUnshared?.Invoke(this, file);
        }
    }

    /// <summary>
    /// Request a peer's file catalog
    /// </summary>
    public async Task RequestCatalogAsync(SpiderId peerId, FileCategory? categoryFilter = null, string? searchQuery = null)
    {
        var message = new CatalogRequestMessage
        {
            SenderId = _node.LocalId.ToString(),
            CategoryFilter = categoryFilter.HasValue ? (int)categoryFilter.Value : null,
            SearchQuery = searchQuery
        };

        await _node.SendMessageAsync(peerId, message);
    }

    /// <summary>
    /// Start downloading a file from the network (BitTorrent-style)
    /// </summary>
    public Task<FileDownload> StartDownloadAsync(SharedFile file, string? destinationPath = null)
    {
        var destPath = destinationPath ?? Path.Combine(_downloadFolder, file.Name);

        // Check if already downloading
        if (_activeDownloads.ContainsKey(file.FileHash))
            throw new InvalidOperationException("File is already being downloaded");

        var download = new FileDownload
        {
            File = file,
            DestinationPath = destPath,
            State = DownloadState.Pending,
            StartedAt = DateTime.UtcNow,
            ChunksCompleted = new bool[file.TotalChunks],
            ChunksInProgress = new bool[file.TotalChunks]
        };

        _activeDownloads[file.FileHash] = download;
        DownloadStarted?.Invoke(this, download);

        // Start download task
        _ = Task.Run(() => DownloadFileAsync(download, _cts!.Token));

        return Task.FromResult(download);
    }

    /// <summary>
    /// Pause a download
    /// </summary>
    public void PauseDownload(string fileHash)
    {
        if (_activeDownloads.TryGetValue(fileHash, out var download))
        {
            download.State = DownloadState.Paused;
        }
    }

    /// <summary>
    /// Resume a paused download
    /// </summary>
    public void ResumeDownload(string fileHash)
    {
        if (_activeDownloads.TryGetValue(fileHash, out var download) && download.State == DownloadState.Paused)
        {
            download.State = DownloadState.Downloading;
            _ = Task.Run(() => DownloadFileAsync(download, _cts!.Token));
        }
    }

    /// <summary>
    /// Cancel a download
    /// </summary>
    public void CancelDownload(string fileHash)
    {
        if (_activeDownloads.TryRemove(fileHash, out var download))
        {
            download.State = DownloadState.Cancelled;

            if (File.Exists(download.DestinationPath))
            {
                try
                {
                    File.Delete(download.DestinationPath);
                }
                catch
                {
                }
            }
        }
    }

    private async Task DownloadFileAsync(FileDownload download, CancellationToken ct)
    {
        download.State = DownloadState.Downloading;
        var speedTracker = new SpeedTracker();

        try
        {
            // Find peers that have this file
            var providers = await FindFileProvidersAsync(download.File.FileHash);
            if (providers.Count == 0)
            {
                download.State = DownloadState.Failed;
                download.ErrorMessage = "No peers have this file";
                DownloadFailed?.Invoke(this, download);
                return;
            }

            download.SourcePeers = providers.Select(p => p.ToString()).ToList();

            // Create or open the destination file
            using var fileStream = new FileStream(download.DestinationPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            fileStream.SetLength(download.File.Size);

            // Download chunks from multiple peers in parallel
            var chunkTasks = new List<Task>();
            var providerIndex = 0;

            for (int i = 0; i < download.File.TotalChunks; i++)
            {
                if (ct.IsCancellationRequested || download.State == DownloadState.Cancelled)
                    break;

                while (download.State == DownloadState.Paused)
                {
                    await Task.Delay(500, ct);
                }

                if (download.ChunksCompleted[i])
                    continue;

                var chunkIndex = i;
                var provider = providers[providerIndex % providers.Count];
                providerIndex++;

                await _downloadSemaphore.WaitAsync(ct);

                chunkTasks.Add(
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                download.ChunksInProgress[chunkIndex] = true;

                                var chunkData = await RequestChunkAsync(provider, download.File.FileHash, chunkIndex, ct);
                                if (chunkData != null)
                                {
                                    // Verify chunk hash
                                    var actualHash = Convert.ToHexString(SHA256.HashData(chunkData)).ToLowerInvariant();
                                    if (actualHash == download.File.ChunkHashes[chunkIndex])
                                    {
                                        lock (fileStream)
                                        {
                                            var offset = (long)chunkIndex * download.File.ChunkSize;
                                            fileStream.Position = offset;
                                            fileStream.Write(chunkData);
                                        }

                                        download.ChunksCompleted[chunkIndex] = true;
                                        download.BytesDownloaded += chunkData.Length;
                                        speedTracker.AddBytes(chunkData.Length);
                                        download.SpeedBytesPerSecond = speedTracker.GetSpeed();

                                        DownloadProgress?.Invoke(this, download);
                                    }
                                }
                            }
                            finally
                            {
                                download.ChunksInProgress[chunkIndex] = false;
                                _downloadSemaphore.Release();
                            }
                        },
                        ct));

                // Limit concurrent chunk downloads
                if (chunkTasks.Count >= 10)
                {
                    await Task.WhenAny(chunkTasks);
                    chunkTasks.RemoveAll(t => t.IsCompleted);
                }
            }

            // Wait for remaining chunks
            await Task.WhenAll(chunkTasks);

            // Verify download
            if (download.ChunksCompleted.All(c => c))
            {
                download.State = DownloadState.Completed;
                download.BytesDownloaded = download.File.Size;
                DownloadCompleted?.Invoke(this, download);

                // Add to our shared files as a seeder
                download.File.LocalPath = download.DestinationPath;
                _sharedFiles[download.File.FileHash] = download.File;
            }
            else
            {
                download.State = DownloadState.Failed;
                download.ErrorMessage = "Some chunks failed to download";
                DownloadFailed?.Invoke(this, download);
            }
        }
        catch (OperationCanceledException)
        {
            download.State = DownloadState.Cancelled;
        }
        catch (Exception ex)
        {
            download.State = DownloadState.Failed;
            download.ErrorMessage = ex.Message;
            DownloadFailed?.Invoke(this, download);
        }
    }

    private async Task<List<SpiderId>> FindFileProvidersAsync(string fileHash)
    {
        var providers = new List<SpiderId>();

        // Check if we already know providers
        if (_fileProviders.TryGetValue(fileHash, out var knownProviders))
        {
            foreach (var peerId in knownProviders)
            {
                if (SpiderId.TryParse(peerId, out var id) && id != null)
                {
                    providers.Add(id);
                }
            }
        }

        // Check owner from file metadata
        var file = _peerCatalogs.Values
            .SelectMany(c => c.Files)
            .FirstOrDefault(f => f.FileHash == fileHash);

        if (file != null && SpiderId.TryParse(file.OwnerPeerId, out var ownerId) && ownerId != null)
        {
            if (!providers.Contains(ownerId))
            {
                providers.Add(ownerId);
            }
        }

        // Ask connected peers if they have the file
        foreach (var peer in _node.ConnectedPeers)
        {
            if (!providers.Contains(peer))
            {
                var availability = await RequestFileAvailabilityAsync(peer, fileHash);
                if (availability != null && availability.AvailableChunks.Count > 0)
                {
                    providers.Add(peer);
                    _fileProviders.GetOrAdd(fileHash, _ => []).Add(peer.ToString());
                }
            }
        }

        return providers;
    }

    private async Task<byte[]?> RequestChunkAsync(SpiderId peerId, string fileHash, int chunkIndex, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<byte[]?>();
        var requestId = Guid.NewGuid().ToString("N");

        void Handler(object? sender, (SpiderId Sender, Message Message) e)
        {
            if (e.Message is P2PChunkResponseMessage response &&
                response.RequestId == requestId &&
                response.FileHash == fileHash &&
                response.ChunkIndex == chunkIndex)
            {
                tcs.TrySetResult(response.Data);
            }
        }

        _node.MessageReceived += Handler;

        try
        {
            var request = new P2PChunkRequestMessage
            {
                Id = requestId,
                SenderId = _node.LocalId.ToString(),
                FileHash = fileHash,
                ChunkIndices = [chunkIndex]
            };

            await _node.SendMessageAsync(peerId, request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _node.MessageReceived -= Handler;
        }
    }

    private async Task<FileAvailabilityMessage?> RequestFileAvailabilityAsync(SpiderId peerId, string fileHash)
    {
        var tcs = new TaskCompletionSource<FileAvailabilityMessage?>();

        void Handler(object? sender, (SpiderId Sender, Message Message) e)
        {
            if (e.Message is FileAvailabilityMessage availability &&
                availability.FileHash == fileHash)
            {
                tcs.TrySetResult(availability);
            }
        }

        _node.MessageReceived += Handler;

        try
        {
            // We need to send a request for availability - in this case we use a catalog request for the specific file
            var request = new CatalogRequestMessage
            {
                SenderId = _node.LocalId.ToString(),
                SearchQuery = fileHash
            };

            await _node.SendMessageAsync(peerId, request);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    return await tcs.Task;
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
            }
        }
        finally
        {
            _node.MessageReceived -= Handler;
        }
    }

    private void OnMessageReceived(object? sender, (SpiderId Sender, Message Message) e)
    {
        Task.Run(() => HandleMessageAsync(e.Sender, e.Message));
    }

    private async Task HandleMessageAsync(SpiderId sender, Message message)
    {
        switch (message)
        {
            case CatalogRequestMessage request:
                await HandleCatalogRequestAsync(sender, request);
                break;

            case CatalogResponseMessage response:
                HandleCatalogResponse(sender, response);
                break;

            case P2PChunkRequestMessage request:
                await HandleChunkRequestAsync(sender, request);
                break;

            case FileAvailabilityMessage availability:
                HandleFileAvailability(sender, availability);
                break;
        }
    }

    private async Task HandleCatalogRequestAsync(SpiderId sender, CatalogRequestMessage request)
    {
        var files = _sharedFiles.Values.AsEnumerable();

        // Apply category filter
        if (request.CategoryFilter.HasValue)
        {
            var category = (FileCategory)request.CategoryFilter.Value;
            files = files.Where(f => f.Category == category);
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(request.SearchQuery))
        {
            var query = request.SearchQuery.ToLowerInvariant();
            files = files.Where(f =>
                f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.FileHash.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                f.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (f.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var fileList = files.Skip(request.Page * request.PageSize).Take(request.PageSize).ToList();

        var response = new CatalogResponseMessage
        {
            SenderId = _node.LocalId.ToString(),
            RequestId = request.Id,
            PeerName = Environment.MachineName,
            TotalFiles = _sharedFiles.Count,
            TotalSize = _sharedFiles.Values.Sum(f => f.Size),
            Files = fileList.Select(f => new SharedFileInfo
            {
                FileHash = f.FileHash,
                Name = f.Name,
                Extension = f.Extension,
                Size = f.Size,
                Description = f.Description,
                Category = (int)f.Category,
                Tags = f.Tags,
                ThumbnailBase64 = f.ThumbnailBase64,
                ThumbnailMimeType = f.ThumbnailMimeType,
                ChunkSize = f.ChunkSize,
                TotalChunks = f.TotalChunks,
                ChunkHashes = f.ChunkHashes,
                SharedAt = new DateTimeOffset(f.SharedAt).ToUnixTimeMilliseconds()
            }).ToList()
        };

        await _node.SendMessageAsync(sender, response);
    }

    private void HandleCatalogResponse(SpiderId sender, CatalogResponseMessage response)
    {
        var catalog = new SharedCatalog
        {
            PeerId = sender.ToString(),
            PeerName = response.PeerName,
            LastUpdated = DateTime.UtcNow,
            Files = response.Files.Select(f => new SharedFile
            {
                FileHash = f.FileHash,
                Name = f.Name,
                Extension = f.Extension,
                Size = f.Size,
                Description = f.Description,
                Category = (FileCategory)f.Category,
                Tags = f.Tags,
                ThumbnailBase64 = f.ThumbnailBase64,
                ThumbnailMimeType = f.ThumbnailMimeType,
                ChunkSize = f.ChunkSize,
                TotalChunks = f.TotalChunks,
                ChunkHashes = f.ChunkHashes,
                SharedAt = DateTimeOffset.FromUnixTimeMilliseconds(f.SharedAt).UtcDateTime,
                OwnerPeerId = sender.ToString(),
                OwnerName = response.PeerName
            }).ToList()
        };

        _peerCatalogs[sender.ToString()] = catalog;

        // Track file providers
        foreach (var file in catalog.Files)
        {
            _fileProviders.GetOrAdd(file.FileHash, _ => []).Add(sender.ToString());
        }

        CatalogReceived?.Invoke(this, catalog);
    }

    private async Task HandleChunkRequestAsync(SpiderId sender, P2PChunkRequestMessage request)
    {
        if (!_sharedFiles.TryGetValue(request.FileHash, out var file) || string.IsNullOrEmpty(file.LocalPath))
            return;

        foreach (var chunkIndex in request.ChunkIndices)
        {
            if (chunkIndex < 0 || chunkIndex >= file.TotalChunks)
                continue;

            try
            {
                var buffer = new byte[file.ChunkSize];
                int bytesRead;

                using (var fs = File.OpenRead(file.LocalPath))
                {
                    fs.Position = (long)chunkIndex * file.ChunkSize;
                    bytesRead = await fs.ReadAsync(buffer.AsMemory());
                }

                var chunkData = bytesRead < buffer.Length ? buffer[..bytesRead] : buffer;
                var chunkHash = Convert.ToHexString(SHA256.HashData(chunkData)).ToLowerInvariant();

                var response = new P2PChunkResponseMessage
                {
                    SenderId = _node.LocalId.ToString(),
                    RequestId = request.Id,
                    FileHash = request.FileHash,
                    ChunkIndex = chunkIndex,
                    Data = chunkData,
                    ChunkHash = chunkHash,
                    HasMore = chunkIndex < request.ChunkIndices.Max()
                };

                await _node.SendMessageAsync(sender, response);
            }
            catch (Exception)
            {
                // Skip failed chunks
            }
        }
    }

    private void HandleFileAvailability(SpiderId sender, FileAvailabilityMessage availability)
    {
        if (availability.AvailableChunks.Count > 0)
        {
            _fileProviders.GetOrAdd(availability.FileHash, _ => []).Add(sender.ToString());
        }
    }

    private async Task GenerateThumbnailAsync(SharedFile file, string filePath)
    {
        // Only generate thumbnails for images
        if (file.Category != FileCategory.Image)
            return;

        try
        {
            // Read first 100KB for thumbnail generation
            var buffer = new byte[Math.Min(100 * 1024, file.Size)];
            int bytesRead;
            using (var fs = File.OpenRead(filePath))
            {
                bytesRead = await fs.ReadAsync(buffer);
            }

            // For simplicity, just use the first part of the image as thumbnail
            // In a real app, you'd use an image processing library to resize
            file.ThumbnailBase64 = Convert.ToBase64String(buffer, 0, bytesRead);
            file.ThumbnailMimeType = GetMimeType(file.Extension);
        }
        catch
        {
            // Ignore thumbnail generation errors
        }
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private async Task LoadSharedFilesAsync()
    {
        var metaFiles = Directory.GetFiles(_sharedFolder, "*.meta.json");
        foreach (var metaFile in metaFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile);
                var file = System.Text.Json.JsonSerializer.Deserialize<SharedFile>(json);
                if (file != null && !string.IsNullOrEmpty(file.LocalPath) && File.Exists(file.LocalPath))
                {
                    _sharedFiles[file.FileHash] = file;
                }
            }
            catch
            {
                // Skip invalid metadata files
            }
        }
    }

    private async Task SaveSharedFileMetadataAsync(SharedFile file)
    {
        var metadataPath = Path.Combine(_sharedFolder, $"{file.FileHash}.meta.json");
        var json = System.Text.Json.JsonSerializer.Serialize(file, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metadataPath, json);
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _cts?.Dispose();
        _downloadSemaphore.Dispose();
    }
}

/// <summary>
/// Tracks download speed over a sliding window
/// </summary>
internal class SpeedTracker
{
    private readonly Queue<(DateTime Time, long Bytes)> _samples = new();
    private readonly TimeSpan _window = TimeSpan.FromSeconds(5);

    public void AddBytes(long bytes)
    {
        _samples.Enqueue((DateTime.UtcNow, bytes));
        Cleanup();
    }

    public double GetSpeed()
    {
        Cleanup();
        if (_samples.Count < 2)
            return 0;

        var totalBytes = _samples.Sum(s => s.Bytes);
        var duration = (DateTime.UtcNow - _samples.Peek().Time).TotalSeconds;
        return duration > 0 ? totalBytes / duration : 0;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - _window;
        while (_samples.Count > 0 && _samples.Peek().Time < cutoff)
        {
            _samples.Dequeue();
        }
    }
}
