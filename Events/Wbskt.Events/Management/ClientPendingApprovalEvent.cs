using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPendingApprovalEvent")]
public sealed record ClientPendingApprovalEvent(Guid ClientRefId, Guid PolicyRefId, int WorkspaceId) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
