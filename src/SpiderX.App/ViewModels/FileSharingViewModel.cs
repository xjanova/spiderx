using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Maui.Storage;
using SpiderX.App.Services;
using SpiderX.Core.Models;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

public class FileSharingViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;
    private bool _isLoading;
    private string _searchQuery = string.Empty;
    private FileCategory? _selectedCategory;
    private string _statusMessage = "Share and download files with your contacts";
    private ObservableCollection<SharedFileItem> _mySharedFiles = [];
    private ObservableCollection<SharedFileItem> _availableFiles = [];
    private ObservableCollection<DownloadItem> _activeDownloads = [];
    private ObservableCollection<PeerCatalogItem> _peerCatalogs = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                FilterFiles();
            }
        }
    }

    public FileCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                FilterFiles();
            }
        }
    }

    private CategoryOption? _selectedCategoryOption;
    public CategoryOption? SelectedCategoryOption
    {
        get => _selectedCategoryOption;
        set
        {
            if (SetProperty(ref _selectedCategoryOption, value))
            {
                SelectedCategory = value?.Category;
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<SharedFileItem> MySharedFiles
    {
        get => _mySharedFiles;
        set => SetProperty(ref _mySharedFiles, value);
    }

    public ObservableCollection<SharedFileItem> AvailableFiles
    {
        get => _availableFiles;
        set => SetProperty(ref _availableFiles, value);
    }

    public ObservableCollection<DownloadItem> ActiveDownloads
    {
        get => _activeDownloads;
        set => SetProperty(ref _activeDownloads, value);
    }

    public ObservableCollection<PeerCatalogItem> PeerCatalogs
    {
        get => _peerCatalogs;
        set => SetProperty(ref _peerCatalogs, value);
    }

    public List<CategoryOption> Categories { get; } =
    [
        new CategoryOption { Name = "All", Category = null },
        new CategoryOption { Name = "🎬 Video", Category = FileCategory.Video },
        new CategoryOption { Name = "🎵 Audio", Category = FileCategory.Audio },
        new CategoryOption { Name = "🖼️ Images", Category = FileCategory.Image },
        new CategoryOption { Name = "📄 Documents", Category = FileCategory.Document },
        new CategoryOption { Name = "📦 Archives", Category = FileCategory.Archive },
        new CategoryOption { Name = "💿 Software", Category = FileCategory.Software },
        new CategoryOption { Name = "🎮 Games", Category = FileCategory.Game },
        new CategoryOption { Name = "📚 Ebooks", Category = FileCategory.Ebook }
    ];

    public ICommand ShareFileCommand { get; }
    public ICommand ShareFolderCommand { get; }
    public ICommand UnshareFileCommand { get; }
    public ICommand DownloadFileCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand PauseDownloadCommand { get; }
    public ICommand ResumeDownloadCommand { get; }
    public ICommand RefreshCatalogsCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public FileSharingViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;

        ShareFileCommand = new Command(async () => await ShareFileAsync());
        ShareFolderCommand = new Command(async () => await ShareFolderAsync());
        UnshareFileCommand = new Command<SharedFileItem>(UnshareFile);
        DownloadFileCommand = new Command<SharedFileItem>(async (f) => await DownloadFileAsync(f));
        CancelDownloadCommand = new Command<DownloadItem>(CancelDownload);
        PauseDownloadCommand = new Command<DownloadItem>(PauseDownload);
        ResumeDownloadCommand = new Command<DownloadItem>(ResumeDownload);
        RefreshCatalogsCommand = new Command(async () => await RefreshCatalogsAsync());
        OpenFileCommand = new Command<SharedFileItem>(OpenFile);
        OpenFolderCommand = new Command<SharedFileItem>(OpenFolder);

        // Subscribe to events
        if (_spiderXService.FileSharing != null)
        {
            _spiderXService.FileSharing.FileShared += OnFileShared;
            _spiderXService.FileSharing.FileUnshared += OnFileUnshared;
            _spiderXService.FileSharing.DownloadStarted += OnDownloadStarted;
            _spiderXService.FileSharing.DownloadProgress += OnDownloadProgress;
            _spiderXService.FileSharing.DownloadCompleted += OnDownloadCompleted;
            _spiderXService.FileSharing.DownloadFailed += OnDownloadFailed;
            _spiderXService.FileSharing.CatalogReceived += OnCatalogReceived;

            // Load existing shared files
            LoadSharedFiles();
        }
    }

    private void LoadSharedFiles()
    {
        if (_spiderXService.FileSharing == null)
            return;

        MySharedFiles.Clear();
        foreach (var file in _spiderXService.FileSharing.SharedFiles.Values)
        {
            MySharedFiles.Add(new SharedFileItem(file, isOwner: true));
        }

        // Load existing downloads
        foreach (var download in _spiderXService.FileSharing.ActiveDownloads.Values)
        {
            ActiveDownloads.Add(new DownloadItem(download));
        }
    }

    private async Task ShareFileAsync()
    {
        if (_spiderXService.FileSharing == null)
        {
            StatusMessage = "File sharing service not available";
            return;
        }

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a file to share"
            });

            if (result == null)
                return;

            IsLoading = true;
            StatusMessage = "Sharing file...";

            var sharedFile = await _spiderXService.FileSharing.ShareFileAsync(result.FullPath);

            StatusMessage = $"Shared: {sharedFile.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sharing file: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShareFolderAsync()
    {
        if (_spiderXService.FileSharing == null)
        {
            StatusMessage = "File sharing service not available";
            return;
        }

        try
        {
            var result = await FolderPicker.Default.PickAsync(new CancellationToken());
            if (result == null || !result.IsSuccessful)
                return;

            IsLoading = true;
            StatusMessage = "Sharing folder...";

            var files = await _spiderXService.FileSharing.ShareFolderAsync(result.Folder.Path);

            StatusMessage = $"Shared {files.Count} files";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sharing folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UnshareFile(SharedFileItem? item)
    {
        if (item == null || _spiderXService.FileSharing == null)
            return;

        _spiderXService.FileSharing.UnshareFile(item.FileHash);
        StatusMessage = $"Unshared: {item.Name}";
    }

    private async Task DownloadFileAsync(SharedFileItem? item)
    {
        if (item == null || _spiderXService.FileSharing == null)
        {
            StatusMessage = "Cannot download file";
            return;
        }

        try
        {
            StatusMessage = $"Starting download: {item.Name}";
            await _spiderXService.FileSharing.StartDownloadAsync(item.SharedFile);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting download: {ex.Message}";
        }
    }

    private void CancelDownload(DownloadItem? item)
    {
        if (item == null || _spiderXService.FileSharing == null)
            return;

        _spiderXService.FileSharing.CancelDownload(item.FileHash);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ActiveDownloads.Remove(item);
        });
    }

    private void PauseDownload(DownloadItem? item)
    {
        if (item == null || _spiderXService.FileSharing == null)
            return;
        _spiderXService.FileSharing.PauseDownload(item.FileHash);
    }

    private void ResumeDownload(DownloadItem? item)
    {
        if (item == null || _spiderXService.FileSharing == null)
            return;
        _spiderXService.FileSharing.ResumeDownload(item.FileHash);
    }

    private async Task RefreshCatalogsAsync()
    {
        if (_spiderXService.FileSharing == null || _spiderXService.Node == null)
        {
            StatusMessage = "Service not available";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Fetching catalogs from peers...";

            PeerCatalogs.Clear();
            AvailableFiles.Clear();

            foreach (var peer in _spiderXService.Node.ConnectedPeers)
            {
                await _spiderXService.FileSharing.RequestCatalogAsync(peer, SelectedCategory, SearchQuery);
            }

            await Task.Delay(2000); // Wait for responses

            StatusMessage = $"Found {AvailableFiles.Count} files from {PeerCatalogs.Count} peers";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterFiles()
    {
        // Apply filters to available files
        if (_spiderXService.FileSharing == null)
            return;

        AvailableFiles.Clear();

        foreach (var catalog in _spiderXService.FileSharing.PeerCatalogs.Values)
        {
            var files = catalog.Files.AsEnumerable();

            if (SelectedCategory.HasValue)
                files = files.Where(f => f.Category == SelectedCategory.Value);

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var query = SearchQuery.ToLowerInvariant();
                files = files.Where(f =>
                    f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (f.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            foreach (var file in files)
            {
                AvailableFiles.Add(new SharedFileItem(file, isOwner: false, peerName: catalog.PeerName));
            }
        }
    }

    private void OpenFile(SharedFileItem? item)
    {
        if (item?.LocalPath == null)
            return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.LocalPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private void OpenFolder(SharedFileItem? item)
    {
        if (item?.LocalPath == null)
            return;

        try
        {
            var folder = Path.GetDirectoryName(item.LocalPath);
            if (folder != null)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch { }
    }

    private void OnFileShared(object? sender, SharedFile file)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MySharedFiles.Add(new SharedFileItem(file, isOwner: true));
        });
    }

    private void OnFileUnshared(object? sender, SharedFile file)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = MySharedFiles.FirstOrDefault(f => f.FileHash == file.FileHash);
            if (item != null)
                MySharedFiles.Remove(item);
        });
    }

    private void OnDownloadStarted(object? sender, FileDownload download)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ActiveDownloads.Add(new DownloadItem(download));
        });
    }

    private void OnDownloadProgress(object? sender, FileDownload download)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = ActiveDownloads.FirstOrDefault(d => d.FileHash == download.File.FileHash);
            item?.Update(download);
        });
    }

    private void OnDownloadCompleted(object? sender, FileDownload download)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = ActiveDownloads.FirstOrDefault(d => d.FileHash == download.File.FileHash);
            if (item != null)
            {
                item.Update(download);
                StatusMessage = $"Download completed: {download.File.Name}";
            }
        });
    }

    private void OnDownloadFailed(object? sender, FileDownload download)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = ActiveDownloads.FirstOrDefault(d => d.FileHash == download.File.FileHash);
            if (item != null)
            {
                item.Update(download);
                StatusMessage = $"Download failed: {download.File.Name} - {download.ErrorMessage}";
            }
        });
    }

    private void OnCatalogReceived(object? sender, SharedCatalog catalog)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = PeerCatalogs.FirstOrDefault(p => p.PeerId == catalog.PeerId);
            if (existing != null)
                PeerCatalogs.Remove(existing);

            PeerCatalogs.Add(new PeerCatalogItem(catalog));

            foreach (var file in catalog.Files)
            {
                AvailableFiles.Add(new SharedFileItem(file, isOwner: false, peerName: catalog.PeerName));
            }
        });
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class CategoryOption
{
    public required string Name { get; init; }
    public FileCategory? Category { get; init; }
}

public class SharedFileItem : INotifyPropertyChanged
{
    public SharedFile SharedFile { get; }
    public bool IsOwner { get; }
    public string? PeerName { get; }

    public string FileHash => SharedFile.FileHash;
    public string Name => SharedFile.Name;
    public string SizeDisplay => SharedFile.SizeDisplay;
    public string Icon => SharedFile.Icon;
    public string CategoryDisplay => SharedFile.CategoryDisplay;
    public string? Description => SharedFile.Description;
    public string? LocalPath => SharedFile.LocalPath;
    public bool HasLocalFile => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);
    public string OwnerDisplay => IsOwner ? "You" : (PeerName ?? "Unknown");
    public string SharedAtDisplay => SharedFile.SharedAt.ToString("g");
    public bool HasThumbnail => !string.IsNullOrEmpty(SharedFile.ThumbnailBase64);
    public ImageSource? ThumbnailSource => GetThumbnailSource();

    public event PropertyChangedEventHandler? PropertyChanged;

    public SharedFileItem(SharedFile file, bool isOwner, string? peerName = null)
    {
        SharedFile = file;
        IsOwner = isOwner;
        PeerName = peerName;
    }

    private ImageSource? GetThumbnailSource()
    {
        if (string.IsNullOrEmpty(SharedFile.ThumbnailBase64)) return null;

        try
        {
            var bytes = Convert.FromBase64String(SharedFile.ThumbnailBase64);
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}

public class DownloadItem : INotifyPropertyChanged
{
    private FileDownload _download;

    public string FileHash => _download.File.FileHash;
    public string Name => _download.File.Name;
    public string SizeDisplay => _download.File.SizeDisplay;
    public double Progress => _download.Progress;
    public string ProgressDisplay => _download.ProgressDisplay;
    public string SpeedDisplay => _download.SpeedDisplay;
    public string EtaDisplay => _download.EtaDisplay;
    public string StateDisplay => _download.State.ToString();
    public bool IsDownloading => _download.State == DownloadState.Downloading;
    public bool IsPaused => _download.State == DownloadState.Paused;
    public bool IsCompleted => _download.State == DownloadState.Completed;
    public bool IsFailed => _download.State == DownloadState.Failed;
    public bool CanPause => _download.State == DownloadState.Downloading;
    public bool CanResume => _download.State == DownloadState.Paused;
    public bool CanCancel => _download.State is DownloadState.Downloading or DownloadState.Paused or DownloadState.Pending;
    public int PeerCount => _download.SourcePeers.Count;
    public string ChunksDisplay => $"{_download.CompletedChunks}/{_download.File.TotalChunks}";

    public Color ProgressColor => _download.State switch
    {
        DownloadState.Completed => Colors.Green,
        DownloadState.Failed => Colors.Red,
        DownloadState.Paused => Colors.Orange,
        _ => Color.FromArgb("#8B5CF6")
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public DownloadItem(FileDownload download)
    {
        _download = download;
    }

    public void Update(FileDownload download)
    {
        _download = download;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

public class PeerCatalogItem
{
    public SharedCatalog Catalog { get; }

    public string PeerId => Catalog.PeerId;
    public string PeerName => Catalog.PeerName ?? "Unknown";
    public int FileCount => Catalog.FileCount;
    public string TotalSizeDisplay => Catalog.TotalSizeDisplay;
    public string LastUpdatedDisplay => Catalog.LastUpdated.ToString("g");

    public PeerCatalogItem(SharedCatalog catalog)
    {
        Catalog = catalog;
    }
}
