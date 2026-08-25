using System.Threading.Channels;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// A bounded in-process queue of rendered mail, drained by <see cref="OutboundMailDispatcher"/>.
/// Modelled on the management host's <c>EventLogBuffer</c>.
/// </summary>
/// <remarks>
/// <b>Nothing here ever blocks a caller.</b> A full queue means the relay is wedged, and making a
/// registration wait on that is the one thing this type exists to prevent. The surplus is refused
/// instead, loudly — every message this host sends has a user-triggered way to ask for it again
/// (resend-verification, forgot-password, re-invite), so a refusal is recoverable where a stalled
/// request is not.
/// <para>
/// <b>FullMode.Wait despite never waiting</b>, which reads like a contradiction and is not. Only
/// <c>WriteAsync</c> waits under this mode; <c>TryWrite</c> returns false immediately, which is the
/// signal the caller needs in order to log the drop. The Drop* modes look like the obvious choice
/// and are a trap: under those, <c>TryWrite</c> discards the message and still returns <b>true</b>,
/// so the queue silently swallows mail and no caller can tell. Do not add a <c>WriteAsync</c> here.
/// </para>
/// <para>
/// Queued mail does not survive a restart. That is the accepted cost of not routing tokens through
/// RabbitMQ; see <see cref="IAuthMailer"/> for why they must not go there.
/// </para>
/// </remarks>
internal sealed class OutboundMailQueue
{
    internal const int Capacity = 1_000;

    private readonly Channel<OutboundMail> _channel = Channel.CreateBounded<OutboundMail>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    /// <summary>
    /// Queues a message, or returns false when the queue is saturated. Never blocks and never waits.
    /// </summary>
    public bool TryWrite(OutboundMail mail) => _channel.Writer.TryWrite(mail);

    public IAsyncEnumerable<OutboundMail> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
