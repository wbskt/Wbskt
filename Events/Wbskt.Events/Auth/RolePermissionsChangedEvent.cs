using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record RolePermissionsChangedEvent(int RoleId) : BaseEvent;
