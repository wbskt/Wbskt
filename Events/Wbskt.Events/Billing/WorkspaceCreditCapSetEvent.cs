using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>A workspace's monthly credit cap was set, changed or removed (<see cref="MonthlyCreditCap"/> null).</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkspaceCreditCapSetEvent")]
public sealed record WorkspaceCreditCapSetEvent(int WorkspaceId, decimal? MonthlyCreditCap) : ActorEvent, IWorkspaceContext;
