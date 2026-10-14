using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserPermissionsChangedEvent(int UserId, Guid UserRefId) : BaseEvent, IUserContext;
