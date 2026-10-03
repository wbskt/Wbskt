using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

/// <summary>
/// A client was deleted from the console. Socket hosts close its connection and refuse its
/// still-valid token; the event log keeps its history under <see cref="ClientRefId"/>.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientDeletedEvent")]
public sealed record ClientDeletedEvent(
    Guid ClientRefId,
    int ClientId,
    Guid PolicyRefId,
    int PolicyId,
    int WorkspaceId,
    string Name) : BaseEvent, IClientContext, IPolicyContext;
