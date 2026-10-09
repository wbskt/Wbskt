using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <remarks>Logged in each workspace in <see cref="WorkspaceIds"/>, the ones the user belongs to.</remarks>
[EventCriticality(EventCriticality.Info)]
public sealed record UserLoginSuccessEvent(int UserId, Guid UserRefId, string IpAddress) : BaseEvent, IUserContext, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}

