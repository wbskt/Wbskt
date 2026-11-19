using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Templates;

// A message template is a command someone can send with one click, so who saved, changed or removed
// one is audited like every other workspace resource. Each event carries the template's ref and name.

/// <summary>A message template was saved.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnMessageTemplateCreatedEvent")]
public sealed record MessageTemplateCreatedEvent(Guid TemplateRefId, int WorkspaceId, string Name, string MessageType)
    : ActorEvent, IWorkspaceContext;

/// <summary>A message template was changed; <c>Changes</c> says which fields.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnMessageTemplateUpdatedEvent")]
public sealed record MessageTemplateUpdatedEvent(Guid TemplateRefId, int WorkspaceId, string Name, string MessageType)
    : ActorEvent, IWorkspaceContext;

/// <summary>A message template was deleted.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnMessageTemplateDeletedEvent")]
public sealed record MessageTemplateDeletedEvent(Guid TemplateRefId, int WorkspaceId, string Name, string MessageType)
    : ActorEvent, IWorkspaceContext;
