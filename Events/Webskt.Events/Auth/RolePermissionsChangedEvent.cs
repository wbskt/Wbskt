using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record RolePermissionsChangedEvent(int RoleId) : PermissionEvent;
