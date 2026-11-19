namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// One rendered message, waiting to be handed to the relay: the same content as plain text and as HTML.
/// </summary>
/// <remarks>
/// Rendered before it is queued rather than after, so the queue holds no raw token in a field
/// anything would think to log: by this point the token exists only inside the two bodies,
/// and nothing logs a body.
/// </remarks>
/// <param name="Kind">Which message this is, for log lines: the subject can carry a tenant's name.</param>
/// <param name="ExpiresAtUtc">
/// When the link in it stops working, for a message that is no use after that: a retry that would
/// fall past it drops the message instead. Null for one worth delivering late, such as the notice
/// that an address was changed, which cannot be asked for again.
/// </param>
internal sealed record OutboundMail(string To, string Subject, string TextBody, string HtmlBody, string Kind = "mail", DateTimeOffset? ExpiresAtUtc = null);
