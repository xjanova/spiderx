using System.Collections.Concurrent;
using SpiderX.Core;
using SpiderX.Core.Messages;
using SpiderX.Crypto;
using SpiderX.Transport;

namespace SpiderX.Services;

/// <summary>
/// Service for peer-to-peer voice calls
/// </summary>
public class VoiceService : IDisposable
{
    private readonly SpiderNode _node;
    private readonly ConcurrentDictionary<string, VoiceCall> _activeCalls = new();
    private readonly VoiceServiceOptions _options;
    private bool _disposed;

    /// <summary>
    /// Event raised when an incoming call is received
    /// </summary>
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;

    /// <summary>
    /// Event raised when a call is connected
    /// </summary>
    public event EventHandler<CallEventArgs>? CallConnected;

    /// <summary>
    /// Event raised when a call ends
    /// </summary>
    public event EventHandler<CallEndedEventArgs>? CallEnded;

    /// <summary>
    /// Event raised when voice data is received
    /// </summary>
    public event EventHandler<VoiceDataEventArgs>? VoiceDataReceived;

    /// <summary>
    /// Active calls
    /// </summary>
    public IReadOnlyCollection<VoiceCall> ActiveCalls => _activeCalls.Values.ToList();

    public VoiceService(SpiderNode node, VoiceServiceOptions? options = null)
    {
        _node = node;
        _options = options ?? new VoiceServiceOptions();

        _node.Peers.DataReceived += OnDataReceived;
        _node.Peers.PermissionRequested += OnPermissionRequested;
    }

    /// <summary>
    /// Initiates a voice call to a peer
    /// </summary>
    public async Task<VoiceCall> CallAsync(SpiderId peerId)
    {
        var peer = _node.Peers.GetPeer(peerId);
        if (peer == null || !peer.IsConnected)
            throw new InvalidOperationException("Peer is not connected");

        if (!peer.IsAuthorized || !peer.Permissions.HasFlag(PermissionLevel.VoiceCall))
        {
            // Request permission first
            await _node.RequestPermissionAsync(peerId, "call");
        }

        var callId = Guid.NewGuid().ToString("N");
        var call = new VoiceCall
        {
            Id = callId,
            PeerId = peerId,
            Direction = CallDirection.Outgoing,
            Status = CallStatus.Ringing,
            StartTime = DateTime.UtcNow
        };

        _activeCalls[callId] = call;

        // Send call request
        var request = new PermissionRequestMessage
        {
            PermissionType = "call",
            DisplayName = _node.Id.Address[..16],
            CustomMessage = callId
        };

        await _node.Peers.SendAsync(peerId, request);
        return call;
    }

    /// <summary>
    /// Accepts an incoming call
    /// </summary>
    public async Task AcceptCallAsync(string callId)
    {
        if (!_activeCalls.TryGetValue(callId, out var call))
            throw new InvalidOperationException("Call not found");

        call.Status = CallStatus.Connected;
        call.ConnectedTime = DateTime.UtcNow;

        var response = new PermissionResponseMessage
        {
            RequestId = callId,
            Granted = true
        };

        await _node.Peers.SendAsync(call.PeerId, response);

        CallConnected?.Invoke(this, new CallEventArgs { Call = call });
    }

    /// <summary>
    /// Rejects an incoming call
    /// </summary>
    public async Task RejectCallAsync(string callId)
    {
        if (!_activeCalls.TryGetValue(callId, out var call))
            return;

        call.Status = CallStatus.Rejected;

        var response = new PermissionResponseMessage
        {
            RequestId = callId,
            Granted = false
        };

        await _node.Peers.SendAsync(call.PeerId, response);
        _activeCalls.TryRemove(callId, out _);

        CallEnded?.Invoke(this, new CallEndedEventArgs
        {
            Call = call,
            Reason = CallEndReason.Rejected
        });
    }

    /// <summary>
    /// Ends an active call
    /// </summary>
    public async Task EndCallAsync(string callId)
    {
        if (!_activeCalls.TryRemove(callId, out var call))
            return;

        call.Status = CallStatus.Ended;
        call.EndTime = DateTime.UtcNow;

        // Send end signal
        var endMessage = new PermissionResponseMessage
        {
            RequestId = callId,
            Granted = false
        };

        try
        {
            await _node.Peers.SendAsync(call.PeerId, endMessage);
        }
        catch
        {
            // Peer might be disconnected
        }

        CallEnded?.Invoke(this, new CallEndedEventArgs
        {
            Call = call,
            Reason = CallEndReason.LocalHangup
        });
    }

    /// <summary>
    /// Sends voice data to the active call
    /// </summary>
    public async Task SendVoiceDataAsync(string callId, byte[] audioData)
    {
        if (!_activeCalls.TryGetValue(callId, out var call))
            throw new InvalidOperationException("Call not found");

        if (call.Status != CallStatus.Connected)
            throw new InvalidOperationException("Call is not connected");

        var voiceData = new VoiceDataMessage
        {
            CallId = callId,
            Sequence = call.NextSequence(),
            Data = audioData,
            Codec = _options.Codec
        };

        // Use unreliable delivery for voice (lower latency)
        var peer = _node.Peers.GetPeer(call.PeerId);
        if (peer?.BestConnection != null)
        {
            await peer.BestConnection.SendAsync(voiceData.Serialize(), DeliveryMode.Unreliable);
        }
    }

    /// <summary>
    /// Toggles mute state
    /// </summary>
    public void SetMuted(string callId, bool muted)
    {
        if (_activeCalls.TryGetValue(callId, out var call))
        {
            call.IsMuted = muted;
        }
    }

    private void OnDataReceived(object? sender, PeerDataEventArgs e)
    {
        switch (e.Message)
        {
            case VoiceDataMessage voiceData:
                HandleVoiceData(e.Peer.Id, voiceData);
                break;

            case PermissionResponseMessage response when _activeCalls.ContainsKey(response.RequestId):
                HandleCallResponse(e.Peer.Id, response);
                break;
        }
    }

    private void OnPermissionRequested(object? sender, PermissionRequestEventArgs e)
    {
        if (e.PermissionType == "call" && e.Peer.IsAuthorized)
        {
            var callId = e.RequestId;
            var call = new VoiceCall
            {
                Id = callId,
                PeerId = e.Peer.Id,
                Direction = CallDirection.Incoming,
                Status = CallStatus.Ringing,
                StartTime = DateTime.UtcNow
            };

            _activeCalls[callId] = call;

            IncomingCall?.Invoke(this, new IncomingCallEventArgs
            {
                Call = call,
                CallerName = e.DisplayName
            });
        }
    }

    private void HandleVoiceData(SpiderId senderId, VoiceDataMessage voiceData)
    {
        if (!_activeCalls.TryGetValue(voiceData.CallId, out var call))
            return;

        if (call.PeerId != senderId || call.Status != CallStatus.Connected)
            return;

        // Check sequence for packet ordering
        if (voiceData.Sequence <= call.LastReceivedSequence)
            return; // Old packet, discard

        call.LastReceivedSequence = voiceData.Sequence;

        VoiceDataReceived?.Invoke(this, new VoiceDataEventArgs
        {
            Call = call,
            AudioData = voiceData.Data,
            Sequence = voiceData.Sequence
        });
    }

    private void HandleCallResponse(SpiderId peerId, PermissionResponseMessage response)
    {
        if (!_activeCalls.TryGetValue(response.RequestId, out var call))
            return;

        if (response.Granted)
        {
            call.Status = CallStatus.Connected;
            call.ConnectedTime = DateTime.UtcNow;
            CallConnected?.Invoke(this, new CallEventArgs { Call = call });
        }
        else
        {
            call.Status = CallStatus.Rejected;
            _activeCalls.TryRemove(response.RequestId, out _);
            CallEnded?.Invoke(this, new CallEndedEventArgs
            {
                Call = call,
                Reason = CallEndReason.Rejected
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var call in _activeCalls.Values)
        {
            _ = EndCallAsync(call.Id);
        }

        _node.Peers.DataReceived -= OnDataReceived;
        _node.Peers.PermissionRequested -= OnPermissionRequested;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Voice service options
/// </summary>
public class VoiceServiceOptions
{
    /// <summary>
    /// Gets or sets the audio codec to use.
    /// </summary>
    public string Codec { get; set; } = "opus";

    /// <summary>
    /// Gets or sets the sample rate in Hz.
    /// </summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// Gets or sets the number of audio channels.
    /// </summary>
    public int Channels { get; set; } = 1;

    /// <summary>
    /// Gets or sets the frame size in samples.
    /// </summary>
    public int FrameSize { get; set; } = 960; // 20ms at 48kHz
}

/// <summary>
/// Represents an active voice call
/// </summary>
public class VoiceCall
{
    private int _sequence;

    /// <summary>
    /// Gets the unique call identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the peer ID.
    /// </summary>
    public required SpiderId PeerId { get; init; }

    /// <summary>
    /// Gets the call direction.
    /// </summary>
    public required CallDirection Direction { get; init; }

    /// <summary>
    /// Gets or sets the call status.
    /// </summary>
    public CallStatus Status { get; set; }

    /// <summary>
    /// Gets the call start time.
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// Gets or sets the time when the call was connected.
    /// </summary>
    public DateTime? ConnectedTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the call ended.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the call is muted.
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Gets or sets the last received sequence number.
    /// </summary>
    public int LastReceivedSequence { get; set; }

    /// <summary>
    /// Gets the call duration.
    /// </summary>
    public TimeSpan? Duration => EndTime.HasValue && ConnectedTime.HasValue
        ? EndTime.Value - ConnectedTime.Value
        : ConnectedTime.HasValue
            ? DateTime.UtcNow - ConnectedTime.Value
            : null;

    internal int NextSequence() => Interlocked.Increment(ref _sequence);
}

/// <summary>
/// Call direction
/// </summary>
public enum CallDirection
{
    Incoming,
    Outgoing
}

/// <summary>
/// Call status
/// </summary>
public enum CallStatus
{
    Ringing,
    Connected,
    Ended,
    Rejected,
    Failed
}

/// <summary>
/// Call end reason
/// </summary>
public enum CallEndReason
{
    LocalHangup,
    RemoteHangup,
    Rejected,
    Failed,
    Timeout
}

/// <summary>
/// Event args for incoming call
/// </summary>
public class IncomingCallEventArgs : EventArgs
{
    public required VoiceCall Call { get; init; }
    public string? CallerName { get; init; }
}

/// <summary>
/// Event args for call events
/// </summary>
public class CallEventArgs : EventArgs
{
    public required VoiceCall Call { get; init; }
}

/// <summary>
/// Event args for call ended
/// </summary>
public class CallEndedEventArgs : EventArgs
{
    public required VoiceCall Call { get; init; }
    public required CallEndReason Reason { get; init; }
}

/// <summary>
/// Event args for voice data
/// </summary>
public class VoiceDataEventArgs : EventArgs
{
    public required VoiceCall Call { get; init; }
    public required byte[] AudioData { get; init; }
    public required int Sequence { get; init; }
}
