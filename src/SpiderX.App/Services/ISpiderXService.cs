using SpiderX.Core;
using SpiderX.Crypto;
using SpiderX.Services;

namespace SpiderX.App.Services;

/// <summary>
/// Main service interface for SpiderX functionality
/// </summary>
public interface ISpiderXService
{
    /// <summary>
    /// The underlying SpiderX node
    /// </summary>
    SpiderNode? Node { get; }

    /// <summary>
    /// Chat service
    /// </summary>
    ChatService? Chat { get; }

    /// <summary>
    /// File transfer service
    /// </summary>
    FileTransferService? FileTransfer { get; }

    /// <summary>
    /// Voice call service
    /// </summary>
    VoiceService? Voice { get; }

    /// <summary>
    /// Whether the node is running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Local peer ID
    /// </summary>
    SpiderId? LocalId { get; }

    /// <summary>
    /// Starts the SpiderX node
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the SpiderX node
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets or creates identity from secure storage
    /// </summary>
    Task<KeyPair> GetOrCreateIdentityAsync();

    /// <summary>
    /// Event raised when connection status changes
    /// </summary>
    event EventHandler<bool>? ConnectionStatusChanged;
}

/// <summary>
/// Implementation of ISpiderXService
/// </summary>
public class SpiderXService : ISpiderXService, IDisposable
{
    private SpiderNode? _node;
    private ChatService? _chatService;
    private FileTransferService? _fileTransferService;
    private VoiceService? _voiceService;
    private KeyPair? _keyPair;

    public SpiderNode? Node => _node;
    public ChatService? Chat => _chatService;
    public FileTransferService? FileTransfer => _fileTransferService;
    public VoiceService? Voice => _voiceService;
    public bool IsRunning => _node?.IsRunning ?? false;
    public SpiderId? LocalId => _node?.Id;

    public event EventHandler<bool>? ConnectionStatusChanged;

    public async Task StartAsync()
    {
        if (_node != null)
            return;

        _keyPair = await GetOrCreateIdentityAsync();

        var options = new SpiderNodeOptions
        {
            EnableUdp = true,
            EnableTcp = true,
            EnableLanDiscovery = true,
            UdpPort = 45678,
            TcpPort = 45679
        };

        _node = new SpiderNode(_keyPair, options);

        // Initialize services
        _chatService = new ChatService(_node);

        var downloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpiderX",
            "Downloads");
        _fileTransferService = new FileTransferService(_node, downloadPath);

        _voiceService = new VoiceService(_node);

        // Subscribe to events
        _node.Started += OnNodeStarted;
        _node.Stopped += OnNodeStopped;

        await _node.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_node == null)
            return;

        await _node.StopAsync();

        _chatService?.Dispose();
        _fileTransferService?.Dispose();
        _voiceService?.Dispose();
        _node.Dispose();

        _chatService = null;
        _fileTransferService = null;
        _voiceService = null;
        _node = null;
    }

    public async Task<KeyPair> GetOrCreateIdentityAsync()
    {
        if (_keyPair != null)
            return _keyPair;

        // Try to load from secure storage
        try
        {
            var privateKeyHex = await SecureStorage.Default.GetAsync("spiderx_private_key");
            if (!string.IsNullOrEmpty(privateKeyHex))
            {
                var privateKey = Convert.FromHexString(privateKeyHex);
                _keyPair = KeyPair.FromPrivateKey(privateKey);
                return _keyPair;
            }
        }
        catch
        {
            // Secure storage not available or key not found
        }

        // Generate new identity
        _keyPair = KeyPair.Generate();

        // Save to secure storage
        try
        {
            var privateKeyHex = Convert.ToHexString(_keyPair.ExportPrivateKey());
            await SecureStorage.Default.SetAsync("spiderx_private_key", privateKeyHex);
        }
        catch
        {
            // Secure storage not available
        }

        return _keyPair;
    }

    private void OnNodeStarted(object? sender, EventArgs e)
    {
        ConnectionStatusChanged?.Invoke(this, true);
    }

    private void OnNodeStopped(object? sender, EventArgs e)
    {
        ConnectionStatusChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _keyPair?.Dispose();
        GC.SuppressFinalize(this);
    }
}
