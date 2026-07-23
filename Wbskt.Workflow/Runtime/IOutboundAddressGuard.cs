namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Vets the target of an outbound HTTP call (e.g. an author-defined webhook URL) before the engine
/// connects, so a workflow author cannot point the engine - which sits on the trusted backend
/// network - at loopback, RFC1918, link-local (incl. cloud metadata 169.254.169.254) or other
/// reserved addresses. This is an SSRF mitigation for the outbound WebhookNotification node.
/// </summary>
internal interface IOutboundAddressGuard
{
    ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct);
}

internal readonly record struct OutboundAddressDecision(bool Allowed, string? Reason)
{
    public static OutboundAddressDecision Allow() => new(true, null);

    public static OutboundAddressDecision Block(string reason) => new(false, reason);
}
