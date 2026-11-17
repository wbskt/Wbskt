using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// One way work reaches the engine (a client message, a webhook call, a signal...). A channel owns its
/// key format end to end: the key a published trigger is registered under, and the key an event on
/// the channel is matched by. Keeping both in one class is what keeps them equal.
/// </summary>
public interface ITriggerChannel
{
    /// <summary>The <see cref="InboundEvent.ChannelKind"/> of this channel's events and the TriggerKind of its registrations.</summary>
    string Kind { get; }

    /// <summary>
    /// The event's default correlation: what a bookmark waiting on this channel is keyed by, and what
    /// concurrency groups runs by when the trigger names no correlation of its own.
    /// </summary>
    string CorrelationKey(InboundEvent evt);

    /// <summary>
    /// The registration a node of a live version gets on this channel, or null when the node is not one
    /// of this channel's triggers. Channels that only wake parked branches register nothing.
    /// </summary>
    TriggerRegistrationSpec? Plan(BaseNode node, TriggerPublication publication);
}

/// <summary>The workflow a version is being made live for, and the moment its schedules count from.</summary>
public sealed record TriggerPublication(Guid WorkflowRefId, Guid WorkspaceRef, DateTime Now);

public sealed record TriggerRegistrationSpec(string TriggerKey, WorkflowConcurrencyPolicy ConcurrencyPolicy, string? CorrelationExpression)
{
    public WorkflowExpression? Filter { get; init; }

    public string? WebhookSecret { get; init; }

    /// <summary>The schedule a time trigger ticks on, created with the registration.</summary>
    public PlannedScheduleSpec? Schedule { get; init; }
}

public sealed record PlannedScheduleSpec(string CronOrInterval, DateTime NextFireAt);

public interface ITriggerChannelRegistry
{
    IReadOnlyCollection<ITriggerChannel> All { get; }

    /// <summary>The channel for an event's <see cref="InboundEvent.ChannelKind"/>, or null for a kind no channel claims.</summary>
    ITriggerChannel? Find(string kind);
}
