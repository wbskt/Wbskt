using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

/// <summary>
/// Wbskt support changed something about the tenant or one of its people through the platform admin,
/// where no audit event of its own says so: a staff plan, limits or credits, or a member's sign-in.
/// <see cref="Action"/> is the staff audit's action name (<c>SetTenantPlan</c>, <c>BlockSignIn</c>), and
/// <see cref="TargetKind"/> with <see cref="TargetRefId"/> what it was about (an <c>Account</c> or a
/// <c>Person</c>). It always comes from <see cref="EventSource.Support"/> and names no user: the staff
/// member and their reason stay in the staff audit. A change that has an event of its own (a member
/// suspended or removed, a workspace credit cap) carries the same source on that event instead.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record SupportActionTakenEvent : TenantActorEvent
{
    public SupportActionTakenEvent(string action, string targetKind, Guid targetRefId)
    {
        Action = action;
        TargetKind = targetKind;
        TargetRefId = targetRefId;
        ActorSource = EventSource.Support;
    }

    public string Action { get; init; }

    public string TargetKind { get; init; }

    public Guid TargetRefId { get; init; }
}
