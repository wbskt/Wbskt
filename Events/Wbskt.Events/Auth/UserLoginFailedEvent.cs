using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <remarks>Logged in each workspace in <see cref="WorkspaceIds"/>, the ones the user belongs to.</remarks>
[EventCriticality(EventCriticality.Warning)]
public sealed record UserLoginFailedEvent(int UserId, Guid UserRefId, string IpAddress, string Reason) : BaseEvent, IUserContext, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
