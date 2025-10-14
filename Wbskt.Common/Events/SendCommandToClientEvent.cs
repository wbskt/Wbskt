using Wbskt.EventBus;

namespace Wbskt.Common.Events;

public record SendCommandToClientEvent(int ClientId, string Payload) : IEvent;
