using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// The credits granted for <see cref="Source"/> and <see cref="SourceRef"/> were taken back, as a refunded
/// credit pack's are: what the grant had not paid for stops counting. What it already paid for stays paid.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantCreditGrantRevokedEvent(int TenantId, Guid TenantRefId, string Source, string SourceRef)
    : BaseEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
