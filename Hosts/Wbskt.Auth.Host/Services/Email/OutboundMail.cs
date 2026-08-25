namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// One rendered message, waiting to be handed to the relay.
/// </summary>
/// <remarks>
/// Rendered before it is queued rather than after, so the queue holds no raw token in a field
/// anything would think to log: by this point the token exists only inside <paramref name="HtmlBody"/>,
/// and nothing logs a body.
/// </remarks>
internal sealed record OutboundMail(string To, string Subject, string HtmlBody);
