using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public record ClientDataReceivedEvent(int ClientId, Guid ClientUniqueId, string Payload) : IEvent;
