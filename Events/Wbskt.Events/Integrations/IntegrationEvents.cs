using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Integrations;

// An integration holds a credential for an outside account, so every change to one is audited. Each
// event carries the integration's ref, name and type (the API's name for it) and never a secret
// value: the audit log's raw-event panel shows event data verbatim.

/// <summary>An integration was added.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnIntegrationCreatedEvent")]
public sealed record IntegrationCreatedEvent(Guid IntegrationRefId, int WorkspaceId, string Name, string Type)
    : ActorEvent, IWorkspaceContext;

/// <summary>An integration was renamed or its settings changed; <c>Changes</c> says which fields, never secret ones.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnIntegrationUpdatedEvent")]
public sealed record IntegrationUpdatedEvent(Guid IntegrationRefId, int WorkspaceId, string Name, string Type)
    : ActorEvent, IWorkspaceContext;

/// <summary>An integration's credential was replaced. Names the secret fields that changed, never their values.</summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnIntegrationSecretRotatedEvent")]
public sealed record IntegrationSecretRotatedEvent(Guid IntegrationRefId, int WorkspaceId, string Name, string Type)
    : ActorEvent, IWorkspaceContext;

/// <summary>A saved integration was tested, with the outcome.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnIntegrationTestedEvent")]
public sealed record IntegrationTestedEvent(Guid IntegrationRefId, int WorkspaceId, string Name, string Type, bool Ok, string? Code)
    : ActorEvent, IWorkspaceContext;

/// <summary>An integration was deleted, and its sealed credential with it.</summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnIntegrationDeletedEvent")]
public sealed record IntegrationDeletedEvent(Guid IntegrationRefId, int WorkspaceId, string Name, string Type)
    : ActorEvent, IWorkspaceContext;
