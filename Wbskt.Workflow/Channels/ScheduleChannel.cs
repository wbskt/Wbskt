using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>A cron occurrence of a schedule trigger. Keys: <c>schedule:{scheduledFireId}</c>.</summary>
public sealed class ScheduleChannel : TriggerChannel
{
    public const string Name = "schedule";

    public override string Kind => Name;

    public static string Key(long scheduledFireId) => $"{Name}:{scheduledFireId}";

    public static InboundEvent Event(ScheduledFireRow fire, DateTime now) => new(
        Name,
        [Key(fire.Id)],
        // Derived from the occurrence, not random: if the process dies between the dispatch and the
        // advance, the lease expires and the same occurrence is leased again, and only a stable id
        // lets the dispatcher's idempotency claim drop it.
        OccurrenceEventId(fire),
        new Dictionary<string, JsonElement>
        {
            ["scheduledFireId"] = Json(fire.Id),
            ["definitionRefId"] = Json(fire.WorkflowRefId),
            ["fireAt"] = Json(fire.NextFireAt)
        },
        now);

    public static string OccurrenceEventId(ScheduledFireRow fire) =>
        $"{Name}:{fire.Id}:{fire.NextFireAt:yyyyMMddTHHmmssfff}";

    public override string CorrelationKey(InboundEvent evt) => $"{Name}:{Required(evt, "scheduledFireId")}";

    public override async Task<TriggerRegistrationSpec?> RegisterAsync(BaseNode node, TriggerPublication publication, CancellationToken ct)
    {
        if (node is not ScheduleTriggerNode trigger)
        {
            return null;
        }

        if (!CronParser.TryGetNextOccurrence(trigger.Config.Cron, publication.Clock.GetUtcNow().UtcDateTime, out DateTime nextFireAt))
        {
            throw new InvalidOperationException($"Schedule trigger '{trigger.NodeId}' has an invalid cron expression '{trigger.Config.Cron}'.");
        }

        WorkflowDefinitionRow definition = publication.Definition;
        ScheduledFireRow fire = await publication.ScheduledFires.InsertAsync(trigger.NodeId, definition.Id, definition.RefId, trigger.Config.Cron, nextFireAt, ct);
        return new TriggerRegistrationSpec(Key(fire.Id), WorkflowConcurrencyPolicy.AllowParallel, null);
    }
}
