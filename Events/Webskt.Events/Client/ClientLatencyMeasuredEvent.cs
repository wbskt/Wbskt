using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientLatencyMeasuredEvent(Guid ClientRefId, int WorkspaceId, double RoundTripMs) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
