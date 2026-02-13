using Webskt.Common.Abstraction.Events;

namespace Webskt.Auth.Host.Events;

public record RolePermissionsChangedEvent(int RoleId) : PermissionEvent;
