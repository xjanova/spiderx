using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpiderX.App.Services;
using SpiderX.Core;
using SpiderX.Crypto;

namespace SpiderX.App.ViewModels;

public partial class ContactsViewModel : ObservableObject
{
    private readonly ISpiderXService _spiderXService;

    [ObservableProperty]
    private ObservableCollection<ContactItem> _contacts = [];

    [ObservableProperty]
    private ObservableCollection<ContactItem> _pendingRequests = [];

    [ObservableProperty]
    private bool _isRefreshing;

    public ContactsViewModel(ISpiderXService spiderXService)
    {
        _spiderXService = spiderXService;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_spiderXService.Node != null)
        {
            _spiderXService.Node.Peers.PeerDiscovered += OnPeerDiscovered;
            _spiderXService.Node.Peers.PeerConnected += OnPeerConnected;
            _spiderXService.Node.Peers.PeerDisconnected += OnPeerDisconnected;
            _spiderXService.Node.Peers.PermissionRequested += OnPermissionRequested;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            LoadContacts();
            await Task.Delay(500); // Visual feedback
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task AddContactAsync()
    {
        var result = await Application.Current!.MainPage!.DisplayPromptAsync(
            "Add Contact",
            "Enter SpiderX ID or scan QR code:",
            placeholder: "spx1...");

        if (!string.IsNullOrEmpty(result))
        {
            try
            {
                // Parse and connect
                var peer = await _spiderXService.Node!.ConnectByShareableAddressAsync(result);
                if (peer != null)
                {
                    // Send contact request
                    await _spiderXService.Node.RequestPermissionAsync(peer.Id, "contact");

                    await Application.Current.MainPage.DisplayAlert(
                        "Request Sent",
                        "Contact request sent successfully!",
                        "OK");

                    LoadContacts();
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    $"Failed to add contact: {ex.Message}",
                    "OK");
            }
        }
    }

    [RelayCommand]
    private async Task AcceptRequestAsync(ContactItem contact)
    {
        try
        {
            var peerId = SpiderId.Parse(contact.PeerId);
            _spiderXService.Node!.Peers.AuthorizePeer(peerId, PermissionLevel.All);

            await _spiderXService.Node.RespondToPermissionAsync(peerId, contact.RequestId!, true);

            PendingRequests.Remove(contact);
            LoadContacts();
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task RejectRequestAsync(ContactItem contact)
    {
        try
        {
            var peerId = SpiderId.Parse(contact.PeerId);
            await _spiderXService.Node!.RespondToPermissionAsync(peerId, contact.RequestId!, false);

            PendingRequests.Remove(contact);
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task BlockContactAsync(ContactItem contact)
    {
        var confirm = await Application.Current!.MainPage!.DisplayAlert(
            "Block Contact",
            $"Are you sure you want to block {contact.DisplayName}?",
            "Block", "Cancel");

        if (confirm)
        {
            var peerId = SpiderId.Parse(contact.PeerId);
            _spiderXService.Node!.Peers.BlockPeer(peerId);
            LoadContacts();
        }
    }

    [RelayCommand]
    private async Task ScanQrAsync()
    {
        // TODO: Implement QR scanning
        await Application.Current!.MainPage!.DisplayAlert(
            "Coming Soon",
            "QR scanning will be available soon!",
            "OK");
    }

    private void LoadContacts()
    {
        if (_spiderXService.Node == null) return;

        Contacts.Clear();

        foreach (var peer in _spiderXService.Node.Peers.AuthorizedPeers)
        {
            Contacts.Add(new ContactItem
            {
                PeerId = peer.Id.Address,
                DisplayName = peer.DisplayName ?? peer.Id.Address[..16],
                IsOnline = peer.IsConnected,
                LastSeen = peer.LastSeen
            });
        }
    }

    private void OnPeerDiscovered(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(LoadContacts);
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(LoadContacts);
    }

    private void OnPeerDisconnected(object? sender, PeerEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(LoadContacts);
    }

    private void OnPermissionRequested(object? sender, PermissionRequestEventArgs e)
    {
        if (e.PermissionType == "contact")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PendingRequests.Add(new ContactItem
                {
                    PeerId = e.Peer.Id.Address,
                    DisplayName = e.DisplayName ?? e.Peer.Id.Address[..16],
                    IsOnline = true,
                    LastSeen = DateTime.UtcNow,
                    RequestId = e.RequestId
                });
            });
        }
    }
}

public class ContactItem
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsOnline { get; init; }
    public required DateTime LastSeen { get; init; }
    public string? RequestId { get; init; }

    public string StatusText => IsOnline ? "Online" : GetLastSeenText();

    private string GetLastSeenText()
    {
        var diff = DateTime.UtcNow - LastSeen;
        if (diff.TotalMinutes < 5) return "Just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        return LastSeen.ToString("MMM d");
    }
}
