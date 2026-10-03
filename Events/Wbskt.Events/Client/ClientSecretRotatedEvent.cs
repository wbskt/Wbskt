using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

/// <summary>
/// A client's secret was replaced. Socket hosts close its connection and refuse tokens issued
/// before <see cref="RotatedAt"/>, so only a device holding the new secret gets back in. Never
/// carries the secret.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientSecretRotatedEvent")]
public sealed record ClientSecretRotatedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    DateTime RotatedAt) : BaseEvent, IClientContext;
