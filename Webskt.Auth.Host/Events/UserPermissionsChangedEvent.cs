using Webskt.Common.Abstraction.Events;

namespace Webskt.Auth.Host.Events;

public record UserPermissionsChangedEvent(int UserId) : UserEvent(UserId);
