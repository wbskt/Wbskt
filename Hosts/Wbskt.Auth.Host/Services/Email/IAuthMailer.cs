using Wbskt.Auth.Host.Models;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// The mail this host sends. Every method hands the message to a background dispatcher and returns
/// immediately: a registration or a password reset must not have its latency — or its success —
/// decided by an SMTP relay.
/// </summary>
/// <remarks>
/// Callers pass the <b>raw</b> token, which is why this interface takes one and no event does. Auth
/// events are consumed by the management host's <c>EventLoggerHandler</c>, which persists every
/// message body verbatim to <c>EventLogs.EventData</c> — so a token published on the bus becomes a
/// durable plaintext credential in the database. That is the reason this is an in-process queue
/// rather than a bus consumer.
/// </remarks>
internal interface IAuthMailer
{
    /// <summary>False when no relay is configured. Callers proceed regardless; nothing here is a
    /// precondition for the operation that triggered it.</summary>
    bool IsConfigured { get; }

    ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct);

    ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct);

    ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct);

    ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct);

    /// <summary>The link that confirms a change of address, sent to the new address.</summary>
    ValueTask QueueEmailChangeAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct);

    /// <summary>Tells the old address the account moved to <paramref name="newEmail"/>, with the link that undoes it.</summary>
    ValueTask QueueEmailChangedAsync(string to, string username, string newEmail, string revertToken, DateTime expiresAtUtc, CancellationToken ct);

    /// <summary>A billing notice to one of the tenant's billing admins, with a link to <paramref name="billingUrl"/>.</summary>
    ValueTask QueueBillingNoticeAsync(string to, string username, BillingNotice notice, string billingUrl, CancellationToken ct);
}
