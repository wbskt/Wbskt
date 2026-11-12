using System.Collections.Concurrent;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>
/// A client connection with no transport behind it, for testing the gateway core: it records what is
/// sent and each close it is asked for. The WebSocket host's own connection is tested apart
/// (<see cref="ClientConnectionTests"/>); another transport needs only this.
/// </summary>
internal sealed class FakeClientConnection : ClientConnectionBase
{
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly ConcurrentQueue<CloseRequest> _closes = new();
    private readonly ConcurrentQueue<DisconnectReason> _transportCloses = new();

    public bool Open { get; set; } = true;

    public override bool IsOpen => Open;

    /// <summary>Thrown by every send, as by a socket that went away.</summary>
    public Exception? SendFailure { get; set; }

    public IReadOnlyList<string> Sent => [.. _sent];

    /// <summary>Every close the transport was asked for; <see cref="ClientConnectionBase"/> asks it at most once.</summary>
    public IReadOnlyList<CloseRequest> Closes => [.. _closes];

    /// <summary>The final reason the session closed the transport with; it does so once.</summary>
    public IReadOnlyList<DisconnectReason> TransportCloses => [.. _transportCloses];

    /// <summary>Runs inside <see cref="CloseTransportAsync"/>, to see the session's state at that moment, or to throw.</summary>
    public Action<DisconnectReason>? OnTransportClose { get; set; }

    protected override Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        if (SendFailure is not null)
        {
            return Task.FromException(SendFailure);
        }

        _sent.Enqueue(text);
        return Task.CompletedTask;
    }

    /// <summary>Thrown by a requested close, as by a transport that failed to send it.</summary>
    public Exception? CloseFailure { get; set; }

    protected override Task CloseAsync(DisconnectReason reason, string description)
    {
        if (CloseFailure != null)
        {
            throw CloseFailure;
        }

        _closes.Enqueue(new CloseRequest(reason, description));
        Open = false;
        return Task.CompletedTask;
    }

    public override Task CloseTransportAsync(DisconnectReason reason)
    {
        _transportCloses.Enqueue(reason);
        OnTransportClose?.Invoke(reason);
        Open = false;
        return Task.CompletedTask;
    }
}
