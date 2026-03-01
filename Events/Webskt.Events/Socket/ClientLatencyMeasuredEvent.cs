using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record ClientLatencyMeasuredEvent(Guid ClientRefId, int WorkspaceId, double RoundTripMs) : ClientLifecycleEvent(ClientRefId, WorkspaceId);