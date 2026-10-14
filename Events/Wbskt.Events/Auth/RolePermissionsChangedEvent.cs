using Wbskt.EventBus.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record RolePermissionsChangedEvent(int RoleId) : BaseEvent;
