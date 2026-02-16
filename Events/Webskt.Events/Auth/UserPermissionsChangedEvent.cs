using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

public record UserPermissionsChangedEvent(int UserId) : UserEvent(UserId);
