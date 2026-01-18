using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpiderX.App.Services;

namespace SpiderX.App.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;

    private string _localId = string.Empty;
    private string _shareableAddress = string.Empty;
    private bool _isConnected;
    private int _connectedPeers;
    private bool _enableLanDiscovery = true;
    private bool _enableNotifications = true;
    private string _displayName = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LocalId
    {
        get => _localId;
        set => SetProperty(ref _localId, value);
    }

    public string ShareableAddress
    {
        get => _shareableAddress;
        set => SetProperty(ref _shareableAddress, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public int ConnectedPeers
    {
        get => _connectedPeers;
        set => SetProperty(ref _connectedPeers, value);
    }

    public bool EnableLanDiscovery
    {
        get => _enableLanDiscovery;
        set
        {
            if (SetProperty(ref _enableLanDiscovery, value))
            {
                Preferences.Default.Set("enable_lan_discovery", value);
            }
        }
    }

    public bool EnableNotifications
    {
        get => _enableNotifications;
        set
        {
            if (SetProperty(ref _enableNotifications, value))
            {
                Preferences.Default.Set("enable_notifications", value);
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Version => "1.0.0";

    public ICommand InitializeCommand { get; }
    public ICommand CopyIdCommand { get; }
    public ICommand ShareIdCommand { get; }
    public ICommand ShowQrCodeCommand { get; }
    public ICommand ExportIdentityCommand { get; }
    public ICommand ImportIdentityCommand { get; }
    public ICommand SaveDisplayNameCommand { get; }

    public SettingsViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
        _spiderXService.ConnectionStatusChanged += OnConnectionStatusChanged;

        InitializeCommand = new Command(() => Initialize());
        CopyIdCommand = new Command(async () => await CopyIdAsync());
        ShareIdCommand = new Command(async () => await ShareIdAsync());
        ShowQrCodeCommand = new Command(async () => await ShowQrCodeAsync());
        ExportIdentityCommand = new Command(async () => await ExportIdentityAsync());
        ImportIdentityCommand = new Command(async () => await ImportIdentityAsync());
        SaveDisplayNameCommand = new Command(() => SaveDisplayName());
    }

    private void Initialize()
    {
        if (_spiderXService.Node != null)
        {
            LocalId = _spiderXService.LocalId?.Address ?? "";
            ShareableAddress = _spiderXService.Node.GetShareableAddress();
            IsConnected = _spiderXService.IsRunning;
            ConnectedPeers = _spiderXService.Node.Peers.ConnectedCount;
        }

        // Load saved settings
        DisplayName = Preferences.Default.Get("display_name", "");
        EnableLanDiscovery = Preferences.Default.Get("enable_lan_discovery", true);
        EnableNotifications = Preferences.Default.Get("enable_notifications", true);
    }

    private async Task CopyIdAsync()
    {
        await Clipboard.Default.SetTextAsync(LocalId);
        await Application.Current!.Windows[0].Page!.DisplayAlert("Copied", "ID copied to clipboard", "OK");
    }

    private async Task ShareIdAsync()
    {
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = ShareableAddress,
            Title = "Share SpiderX ID"
        });
    }

    private async Task ShowQrCodeAsync()
    {
        // TODO: Implement QR code display
        await Application.Current!.Windows[0].Page!.DisplayAlert(
            "Your QR Code",
            "QR code generation coming soon!",
            "OK");
    }

    private async Task ExportIdentityAsync()
    {
        var confirm = await Application.Current!.Windows[0].Page!.DisplayAlert(
            "Export Identity",
            "This will export your private key. Keep it safe and never share it!",
            "Export", "Cancel");

        if (confirm)
        {
            try
            {
                var keyPair = await _spiderXService.GetOrCreateIdentityAsync();
                var privateKeyHex = Convert.ToHexString(keyPair.ExportPrivateKey());

                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Text = privateKeyHex,
                    Title = "SpiderX Private Key (KEEP SECRET)"
                });
            }
            catch (Exception ex)
            {
                await Application.Current!.Windows[0].Page!.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    private async Task ImportIdentityAsync()
    {
        var privateKeyHex = await Application.Current!.Windows[0].Page!.DisplayPromptAsync(
            "Import Identity",
            "Enter your private key (hex):",
            placeholder: "Enter hex key...");

        if (!string.IsNullOrEmpty(privateKeyHex))
        {
            try
            {
                // Validate and save
                var privateKey = Convert.FromHexString(privateKeyHex);
                if (privateKey.Length != 32)
                    throw new Exception("Invalid key length");

                await SecureStorage.Default.SetAsync("spiderx_private_key", privateKeyHex);

                await Application.Current!.Windows[0].Page!.DisplayAlert(
                    "Success",
                    "Identity imported. Please restart the app.",
                    "OK");
            }
            catch (Exception ex)
            {
                await Application.Current!.Windows[0].Page!.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    private void SaveDisplayName()
    {
        Preferences.Default.Set("display_name", DisplayName);
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = isConnected;
            if (_spiderXService.Node != null)
            {
                ConnectedPeers = _spiderXService.Node.Peers.ConnectedCount;
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
