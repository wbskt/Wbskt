using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserPermissionsChangedEvent(int UserId, Guid UserRefId) : BaseEvent, IUserContext;
