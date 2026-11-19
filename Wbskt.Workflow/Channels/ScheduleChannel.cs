using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A cron occurrence of a schedule trigger. Keys: <c>schedule:{workflowRefId}:{triggerNodeId}</c>. Only one
/// version of a workflow is live, so the key is unique, and it is known before the schedule's row exists.
/// </summary>
public sealed class ScheduleChannel : TriggerChannel
{
    public const string Name = "schedule";

    public override string Kind => Name;

    public static string Key(Guid workflowRefId, Guid triggerNodeId) => $"{Name}:{workflowRefId}:{triggerNodeId}";

    public static InboundEvent Event(ScheduledFireRow fire, DateTime now) => new(
        Name,
        [Key(fire.WorkflowRefId, fire.TriggerNodeId)],
        // Derived from the occurrence, not random: if the process dies between the dispatch and the
        // advance, the lease expires and the same occurrence is leased again, and only a stable id
        // lets the dispatcher's idempotency claim drop it.
        OccurrenceEventId(fire),
        new Dictionary<string, JsonElement>
        {
            ["definitionRefId"] = Json(fire.WorkflowRefId),
            ["triggerNodeId"] = Json(fire.TriggerNodeId),
            ["fireAt"] = Json(fire.NextFireAt)
        },
        now);

    public static string OccurrenceEventId(ScheduledFireRow fire) =>
        $"{Name}:{fire.Id}:{fire.NextFireAt:yyyyMMddTHHmmssfff}";

    // By trigger rather than by schedule row, which is replaced each time a version is made live: a run
    // started before a switch still holds its slot for the same trigger after it.
    public override string CorrelationKey(InboundEvent evt) => $"{Name}:{Required(evt, "definitionRefId")}:{Required(evt, "triggerNodeId")}";

    public override TriggerRegistrationSpec? Plan(BaseNode node, TriggerPublication publication)
    {
        if (node is not ScheduleTriggerNode trigger)
        {
            return null;
        }

        if (!CronParser.TryGetNextOccurrence(trigger.Config.Cron, publication.Now, out DateTime nextFireAt))
        {
            throw new InvalidOperationException($"Schedule trigger '{trigger.NodeId}' has an invalid cron expression '{trigger.Config.Cron}'.");
        }

        return new TriggerRegistrationSpec(Key(publication.WorkflowRefId, trigger.NodeId), WorkflowConcurrencyPolicy.AllowParallel, null)
        {
            Schedule = new PlannedScheduleSpec(trigger.Config.Cron, nextFireAt)
        };
    }
}
