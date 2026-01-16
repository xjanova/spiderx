using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpiderX.App.Services;

namespace SpiderX.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISpiderXService _spiderXService;

    [ObservableProperty]
    private string _localId = "";

    [ObservableProperty]
    private string _shareableAddress = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _connectedPeers;

    [ObservableProperty]
    private bool _enableLanDiscovery = true;

    [ObservableProperty]
    private bool _enableNotifications = true;

    [ObservableProperty]
    private string _displayName = "";

    public string Version => "1.0.0";

    public SettingsViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
        _spiderXService.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        await Clipboard.Default.SetTextAsync(LocalId);
        await Application.Current!.MainPage!.DisplayAlert("Copied", "ID copied to clipboard", "OK");
    }

    [RelayCommand]
    private async Task ShareIdAsync()
    {
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = ShareableAddress,
            Title = "Share SpiderX ID"
        });
    }

    [RelayCommand]
    private async Task ShowQrCodeAsync()
    {
        // TODO: Implement QR code display
        await Application.Current!.MainPage!.DisplayAlert(
            "Your QR Code",
            "QR code generation coming soon!",
            "OK");
    }

    [RelayCommand]
    private async Task ExportIdentityAsync()
    {
        var confirm = await Application.Current!.MainPage!.DisplayAlert(
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
                await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    [RelayCommand]
    private async Task ImportIdentityAsync()
    {
        var privateKeyHex = await Application.Current!.MainPage!.DisplayPromptAsync(
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

                await Application.Current.MainPage.DisplayAlert(
                    "Success",
                    "Identity imported. Please restart the app.",
                    "OK");
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    [RelayCommand]
    private void SaveDisplayName()
    {
        Preferences.Default.Set("display_name", DisplayName);
    }

    partial void OnEnableLanDiscoveryChanged(bool value)
    {
        Preferences.Default.Set("enable_lan_discovery", value);
    }

    partial void OnEnableNotificationsChanged(bool value)
    {
        Preferences.Default.Set("enable_notifications", value);
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
}
