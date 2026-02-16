using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

public record RolePermissionsChangedEvent(int RoleId) : PermissionEvent;
