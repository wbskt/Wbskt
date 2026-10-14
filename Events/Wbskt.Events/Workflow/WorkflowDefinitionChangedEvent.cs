using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>
/// The definitions or trigger registrations of a workflow changed: a version was published (or rolled
/// back to), deprecated, reinstated or deleted, or a publish that failed part-way was undone. The
/// engine drops what it cached of them in response (<c>WorkflowDefinitionChangedConsumer</c>), so the
/// next message for one of their triggers is answered from the database.
/// </summary>
/// <param name="WorkflowRefId">The workflow, or null when every workflow of the workspace changed at once (the workspace was retired).</param>
/// <param name="WorkspaceId">The workspace the workflow belongs to.</param>
/// <param name="DefinitionIds">The definition versions whose rows or registrations changed.</param>
/// <remarks>
/// Sent after the change is committed, from a queued bus in the management host, so it can arrive
/// late or, if the process stops with it still queued, not at all. The engine's caches therefore keep
/// a bounded lifetime of their own as well.
/// </remarks>
[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowDefinitionChangedEvent(Guid? WorkflowRefId, int WorkspaceId, IReadOnlyList<int> DefinitionIds)
    : BaseEvent, IWorkspaceContext;
