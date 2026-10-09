using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <summary>
/// Something a person did to their own account, logged in each workspace they belong to so its owners
/// see it. Not attributed by the bus: the person is the event's user, signed in or not.
/// </summary>
public abstract record AccountEvent(int UserId, Guid UserRefId) : BaseEvent, IUserContext, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];

    /// <summary>Where the request came from; the event log moves it to its own column.</summary>
    [SignalRPrivate] public string? ClientAddress { get; init; }
}

/// <summary>The password was changed, by the person (<c>changed</c>) or through a reset link (<c>reset</c>).</summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record PasswordChangedEvent(int UserId, Guid UserRefId, string How) : AccountEvent(UserId, UserRefId);

/// <summary>The person signed out of every session (<c>all</c>) or ended one (<c>one</c>).</summary>
[EventCriticality(EventCriticality.Info)]
public sealed record SessionsRevokedEvent(int UserId, Guid UserRefId, string Which) : AccountEvent(UserId, UserRefId);

[EventCriticality(EventCriticality.Info)]
public sealed record EmailVerifiedEvent(int UserId, Guid UserRefId) : AccountEvent(UserId, UserRefId);
