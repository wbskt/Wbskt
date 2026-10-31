using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>A command waiting for an offline device was cancelled before the device came back.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandCancelledEvent")]
public sealed record ClientCommandCancelledEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, Guid CommandId) : ActorEvent, IClientContext;
