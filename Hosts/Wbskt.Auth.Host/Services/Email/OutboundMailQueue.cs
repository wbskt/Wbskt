using System.Threading.Channels;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// A bounded in-process queue of rendered mail, drained by <see cref="OutboundMailDispatcher"/>.
/// Modelled on the management host's <c>EventLogBuffer</c>.
/// </summary>
/// <remarks>
/// <b>DropWrite, not Wait.</b> A full queue means the relay is wedged; blocking the writer would put
/// that latency back on the request path, which is the one thing this type exists to prevent. A drop
/// is logged loudly and is recoverable — every message this host sends has a user-triggered way to
/// ask for it again (resend-verification, forgot-password, re-invite).
/// <para>
/// Queued mail does not survive a restart. That is the accepted cost of not routing tokens through
/// RabbitMQ; see <see cref="IAuthMailer"/> for why they must not go there.
/// </para>
/// </remarks>
internal sealed class OutboundMailQueue
{
    private readonly Channel<OutboundMail> _channel = Channel.CreateBounded<OutboundMail>(
        new BoundedChannelOptions(capacity: 1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    /// <summary>Returns false when the queue is saturated and the message was dropped.</summary>
    public bool TryWrite(OutboundMail mail) => _channel.Writer.TryWrite(mail);

    public IAsyncEnumerable<OutboundMail> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
