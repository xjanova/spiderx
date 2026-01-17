using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpiderX.App.Services;
using SpiderX.Core.Messages;
using SpiderX.Services;

namespace SpiderX.App.ViewModels;

public class VirtualLanViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;

    private bool _isVlanActive;
    private string _virtualIp = "Not active";
    private string _statusMessage = "Virtual LAN is disabled";
    private ObservableCollection<VlanPeerItem> _connectedPeers = [];
    private long _bytesReceived;
    private long _bytesSent;
    private int _gamePortsMonitored;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVlanActive
    {
        get => _isVlanActive;
        set
        {
            if (SetProperty(ref _isVlanActive, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleButtonText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
            }
        }
    }

    public string VirtualIp
    {
        get => _virtualIp;
        set => SetProperty(ref _virtualIp, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<VlanPeerItem> ConnectedPeers
    {
        get => _connectedPeers;
        set => SetProperty(ref _connectedPeers, value);
    }

    public long BytesReceived
    {
        get => _bytesReceived;
        set
        {
            if (SetProperty(ref _bytesReceived, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesReceivedDisplay)));
            }
        }
    }

    public long BytesSent
    {
        get => _bytesSent;
        set
        {
            if (SetProperty(ref _bytesSent, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesSentDisplay)));
            }
        }
    }

    public int GamePortsMonitored
    {
        get => _gamePortsMonitored;
        set => SetProperty(ref _gamePortsMonitored, value);
    }

    public string ToggleButtonText => IsVlanActive ? "Disable Virtual LAN" : "Enable Virtual LAN";
    public string StatusColor => IsVlanActive ? "#22C55E" : "#EF4444";
    public string BytesReceivedDisplay => FormatBytes(BytesReceived);
    public string BytesSentDisplay => FormatBytes(BytesSent);

    public ICommand ToggleVlanCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyIpCommand { get; }

    public VirtualLanViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;

        ToggleVlanCommand = new Command(async () => await ToggleVlanAsync());
        RefreshCommand = new Command(() => RefreshPeers());
        CopyIpCommand = new Command(async () => await CopyIpAsync());

        // Subscribe to VLAN events
        if (_spiderXService.VirtualLan != null)
        {
            _spiderXService.VirtualLan.PeerJoined += OnPeerJoined;
            _spiderXService.VirtualLan.PeerLeft += OnPeerLeft;
            _spiderXService.VirtualLan.TrafficReceived += OnTrafficReceived;

            // Check initial state
            IsVlanActive = _spiderXService.VirtualLan.IsRunning;
            if (IsVlanActive)
            {
                VirtualIp = _spiderXService.VirtualLan.VirtualIp?.ToString() ?? "Not assigned";
                StatusMessage = "Virtual LAN is active - LAN games can now find each other!";
                RefreshPeers();
            }
        }
    }

    private async Task ToggleVlanAsync()
    {
        if (_spiderXService.VirtualLan == null)
        {
            StatusMessage = "Virtual LAN service not available";
            return;
        }

        try
        {
            if (IsVlanActive)
            {
                await _spiderXService.VirtualLan.StopAsync();
                IsVlanActive = false;
                VirtualIp = "Not active";
                StatusMessage = "Virtual LAN disabled";
                ConnectedPeers.Clear();
            }
            else
            {
                StatusMessage = "Starting Virtual LAN...";
                await _spiderXService.VirtualLan.StartAsync();
                IsVlanActive = true;
                VirtualIp = _spiderXService.VirtualLan.VirtualIp?.ToString() ?? "Not assigned";
                StatusMessage = "Virtual LAN is active - LAN games can now find each other!";
                RefreshPeers();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void RefreshPeers()
    {
        if (_spiderXService.VirtualLan == null) return;

        ConnectedPeers.Clear();

        foreach (var peer in _spiderXService.VirtualLan.Peers)
        {
            ConnectedPeers.Add(new VlanPeerItem
            {
                PeerId = peer.PeerId.Address,
                VirtualIp = peer.VirtualIp.ToString(),
                Hostname = peer.Hostname,
                JoinedAt = peer.JoinedAt,
                HasBroadcastRelay = peer.Capabilities.HasFlag(VlanCapabilities.BroadcastRelay),
                HasGameDiscovery = peer.Capabilities.HasFlag(VlanCapabilities.GameDiscovery)
            });
        }
    }

    private async Task CopyIpAsync()
    {
        if (!string.IsNullOrEmpty(VirtualIp) && VirtualIp != "Not active")
        {
            await Clipboard.Default.SetTextAsync(VirtualIp);
            await Application.Current!.Windows[0].Page!.DisplayAlert(
                "Copied",
                $"Virtual IP {VirtualIp} copied to clipboard",
                "OK");
        }
    }

    private void OnPeerJoined(object? sender, VirtualLanPeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectedPeers.Add(new VlanPeerItem
            {
                PeerId = e.Peer.PeerId.Address,
                VirtualIp = e.Peer.VirtualIp.ToString(),
                Hostname = e.Peer.Hostname,
                JoinedAt = e.Peer.JoinedAt,
                HasBroadcastRelay = e.Peer.Capabilities.HasFlag(VlanCapabilities.BroadcastRelay),
                HasGameDiscovery = e.Peer.Capabilities.HasFlag(VlanCapabilities.GameDiscovery)
            });

            StatusMessage = $"{e.Peer.Hostname} joined the Virtual LAN";
        });
    }

    private void OnPeerLeft(object? sender, VirtualLanPeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var peer = ConnectedPeers.FirstOrDefault(p => p.PeerId == e.Peer.PeerId.Address);
            if (peer != null)
            {
                ConnectedPeers.Remove(peer);
            }

            StatusMessage = $"{e.Peer.Hostname} left the Virtual LAN";
        });
    }

    private void OnTrafficReceived(object? sender, VirtualLanTrafficEventArgs e)
    {
        BytesReceived += e.Data.Length;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class VlanPeerItem
{
    public required string PeerId { get; init; }
    public required string VirtualIp { get; init; }
    public required string Hostname { get; init; }
    public required DateTime JoinedAt { get; init; }
    public required bool HasBroadcastRelay { get; init; }
    public required bool HasGameDiscovery { get; init; }

    public string DisplayName => $"{Hostname} ({VirtualIp})";
    public string JoinedTimeDisplay => GetJoinedTimeDisplay();
    public string CapabilitiesDisplay => GetCapabilitiesDisplay();

    private string GetJoinedTimeDisplay()
    {
        var diff = DateTime.UtcNow - JoinedAt;
        if (diff.TotalMinutes < 1) return "Just joined";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        return $"{(int)diff.TotalHours}h ago";
    }

    private string GetCapabilitiesDisplay()
    {
        var caps = new List<string>();
        if (HasBroadcastRelay) caps.Add("Broadcast");
        if (HasGameDiscovery) caps.Add("Game Discovery");
        return caps.Count > 0 ? string.Join(", ", caps) : "Basic";
    }
}
