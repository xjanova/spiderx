using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpiderX.App.Services;
using SpiderX.Core;
using SpiderX.Crypto;
using SpiderX.Transport;

namespace SpiderX.App.ViewModels;

public class NearbyDiscoveryViewModel : INotifyPropertyChanged
{
    private readonly ISpiderXService _spiderXService;

    private ObservableCollection<NearbyPeerItem> _nearbyPeers = [];
    private bool _isScanning;
    private string _statusMessage = "Tap 'Scan' to discover nearby peers";
    private int _scanProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NearbyPeerItem> NearbyPeers
    {
        get => _nearbyPeers;
        set => SetProperty(ref _nearbyPeers, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanScan)));
            }
        }
    }

    public bool CanScan => !IsScanning;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }

    public ICommand ScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }

    private CancellationTokenSource? _scanCts;

    public NearbyDiscoveryViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;

        ScanCommand = new Command(async () => await ScanAsync());
        StopScanCommand = new Command(() => StopScan());
        ConnectCommand = new Command<NearbyPeerItem>(async (peer) => await ConnectToPeerAsync(peer));
        RefreshCommand = new Command(() => RefreshConnectedPeers());
    }

    private async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = "Scanning for nearby peers...";
        NearbyPeers.Clear();

        _scanCts = new CancellationTokenSource();

        try
        {
            // Add already known/connected peers first
            RefreshConnectedPeers();

            // Scan using multiple methods
            var tasks = new List<Task>
            {
                ScanLanAsync(_scanCts.Token),
                ScanKnownPeersAsync(_scanCts.Token)
            };

            // Update progress animation
            _ = AnimateProgressAsync(_scanCts.Token);

            await Task.WhenAll(tasks);

            StatusMessage = NearbyPeers.Count > 0
                ? $"Found {NearbyPeers.Count} peer(s)"
                : "No peers found. Make sure other devices are running SpiderX.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgress = 100;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    private async Task AnimateProgressAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && ScanProgress < 95)
        {
            await Task.Delay(100, cancellationToken);
            ScanProgress = Math.Min(95, ScanProgress + 2);
        }
    }

    private async Task ScanLanAsync(CancellationToken cancellationToken)
    {
        if (_spiderXService.Node == null)
            return;

        try
        {
            // Create a temporary LAN discovery instance for scanning
            var lanDiscovery = new LanDiscovery(_spiderXService.LocalId!, 45678);

            lanDiscovery.PeerDiscovered += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AddOrUpdatePeer(e.PeerId, e.Endpoint, "LAN");
                });
            };

            await lanDiscovery.StartAsync(cancellationToken);
            await lanDiscovery.SearchAsync(cancellationToken);

            // Wait for responses
            await Task.Delay(3000, cancellationToken);

            await lanDiscovery.StopAsync();
            lanDiscovery.Dispose();
        }
        catch (System.Net.Sockets.SocketException)
        {
            // LAN discovery not available
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "LAN discovery unavailable. Checking known peers...";
            });
        }
    }

    private async Task ScanKnownPeersAsync(CancellationToken cancellationToken)
    {
        if (_spiderXService.Node == null)
            return;

        // Get peers from DHT routing table
        var dhtNodes = _spiderXService.Node.Peers.FindClosestPeers(_spiderXService.LocalId!, 50);

        foreach (var node in dhtNodes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var endpoint = new EndpointInfo
            {
                Address = node.Address,
                Port = node.Port,
                TransportType = TransportType.Udp
            };

            // Try to ping
            try
            {
                var peer = await _spiderXService.Node.Peers.ConnectByIdAsync(node.Id, cancellationToken);
                if (peer != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        AddOrUpdatePeer(node.Id, endpoint, "DHT", peer.DisplayName);
                    });
                }
            }
            catch
            {
                // Peer not reachable
            }
        }

        await Task.CompletedTask;
    }

    private void RefreshConnectedPeers()
    {
        if (_spiderXService.Node == null)
            return;

        // Add currently connected peers
        foreach (var peer in _spiderXService.Node.Peers.Peers)
        {
            if (peer.IsConnected)
            {
                var endpoint = peer.KnownEndpoints.FirstOrDefault() ?? new EndpointInfo
                {
                    Address = "Unknown",
                    Port = 0,
                    TransportType = TransportType.Udp
                };

                AddOrUpdatePeer(peer.Id, endpoint, "Connected", peer.DisplayName, isConnected: true);
            }
        }
    }

    private void AddOrUpdatePeer(SpiderId peerId, EndpointInfo endpoint, string discoverySource, string? displayName = null, bool isConnected = false)
    {
        // Skip self
        if (peerId == _spiderXService.LocalId)
            return;

        var existingPeer = NearbyPeers.FirstOrDefault(p => p.PeerId == peerId.Address);
        if (existingPeer != null)
        {
            existingPeer.IsConnected = isConnected || existingPeer.IsConnected;
            existingPeer.DiscoverySource = discoverySource;
            return;
        }

        NearbyPeers.Add(new NearbyPeerItem
        {
            PeerId = peerId.Address,
            DisplayName = displayName ?? peerId.Address[..16],
            IpAddress = endpoint.Address,
            Port = endpoint.Port,
            DiscoverySource = discoverySource,
            IsConnected = isConnected,
            SignalStrength = CalculateSignalStrength(endpoint)
        });
    }

    private int CalculateSignalStrength(EndpointInfo endpoint)
    {
        // Simple signal strength estimation based on IP locality
        if (endpoint.Address.StartsWith("192.168.") || endpoint.Address.StartsWith("10."))
            return 100; // Local network
        if (endpoint.Address.StartsWith("172."))
            return 80; // Private network
        return 60; // Remote
    }

    private async Task ConnectToPeerAsync(NearbyPeerItem peerItem)
    {
        if (_spiderXService.Node == null)
            return;

        peerItem.IsConnecting = true;

        try
        {
            var peerId = SpiderId.Parse(peerItem.PeerId);
            var peer = await _spiderXService.Node.Peers.ConnectByIdAsync(peerId);

            if (peer != null)
            {
                peerItem.IsConnected = true;
                peerItem.IsConnecting = false;

                await Application.Current!.Windows[0].Page!.DisplayAlert(
                    "Connected",
                    $"Successfully connected to {peerItem.DisplayName}",
                    "OK");

                // Optionally send contact request
                var sendRequest = await Application.Current!.Windows[0].Page!.DisplayAlert(
                    "Add Contact?",
                    $"Would you like to add {peerItem.DisplayName} to your contacts?",
                    "Yes", "No");

                if (sendRequest)
                {
                    await _spiderXService.Node.RequestPermissionAsync(peerId, "contact");
                    await Application.Current!.Windows[0].Page!.DisplayAlert(
                        "Request Sent",
                        "Contact request sent!",
                        "OK");
                }
            }
            else
            {
                peerItem.IsConnecting = false;
                await Application.Current!.Windows[0].Page!.DisplayAlert(
                    "Connection Failed",
                    "Could not connect to the peer. They may be offline or unreachable.",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            peerItem.IsConnecting = false;
            await Application.Current!.Windows[0].Page!.DisplayAlert(
                "Error",
                $"Connection error: {ex.Message}",
                "OK");
        }
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class NearbyPeerItem : INotifyPropertyChanged
{
    private bool _isConnected;
    private bool _isConnecting;
    private string _discoverySource = "";

    public required string PeerId { get; init; }
    public required string DisplayName { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }
    public required int SignalStrength { get; init; }

    public string DiscoverySource
    {
        get => _discoverySource;
        set
        {
            _discoverySource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscoverySource)));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConnect)));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _isConnecting = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnecting)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConnect)));
        }
    }

    public bool CanConnect => !IsConnected && !IsConnecting;

    public string StatusText
    {
        get
        {
            if (IsConnected)
                return "Connected";
            if (IsConnecting)
                return "Connecting...";
            return $"via {DiscoverySource}";
        }
    }

    public string SignalIcon
    {
        get
        {
            if (SignalStrength >= 80)
                return "signal_full.png";
            if (SignalStrength >= 50)
                return "signal_medium.png";
            return "signal_low.png";
        }
    }

    public string AddressDisplay => $"{IpAddress}:{Port}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
