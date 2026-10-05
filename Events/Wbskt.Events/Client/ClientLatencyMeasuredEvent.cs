using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientLatencyMeasuredEvent")]
public sealed record ClientLatencyMeasuredEvent(Guid ClientRefId, int ClientId, int WorkspaceId, double RoundTripMs) : BaseEvent, IClientContext;
