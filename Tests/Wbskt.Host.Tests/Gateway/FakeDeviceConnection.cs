using System.Collections.Concurrent;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>
/// A device connection with no transport behind it, for testing the gateway core: it records what is
/// sent and each close it is asked for. The WebSocket host's own connection is tested apart
/// (<see cref="ClientConnectionTests"/>); another transport needs only this.
/// </summary>
internal sealed class FakeDeviceConnection : DeviceConnection
{
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly ConcurrentQueue<CloseRequest> _closes = new();

    public bool Open { get; set; } = true;

    public override bool IsOpen => Open;

    /// <summary>Thrown by every send, as by a socket that went away.</summary>
    public Exception? SendFailure { get; set; }

    public IReadOnlyList<string> Sent => [.. _sent];

    /// <summary>Every close the transport was asked for; <see cref="DeviceConnection"/> asks it at most once.</summary>
    public IReadOnlyList<CloseRequest> Closes => [.. _closes];

    protected override Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        if (SendFailure is not null)
        {
            return Task.FromException(SendFailure);
        }

        _sent.Enqueue(text);
        return Task.CompletedTask;
    }

    protected override Task CloseAsync(DisconnectReason reason, string description)
    {
        _closes.Enqueue(new CloseRequest(reason, description));
        Open = false;
        return Task.CompletedTask;
    }
}
