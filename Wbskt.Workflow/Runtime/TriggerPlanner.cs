using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class TriggerPlanner : ITriggerPlanner
{
    private readonly ITriggerChannelRegistry _channels;
    private readonly TimeProvider _clock;

    public TriggerPlanner(ITriggerChannelRegistry channels, TimeProvider clock)
    {
        _channels = channels;
        _clock = clock;
    }

    public TriggerPlan Plan(WorkflowDefinition definition, Guid workflowRefId, Guid workspaceRef)
    {
        var publication = new TriggerPublication(workflowRefId, workspaceRef, _clock.GetUtcNow().UtcDateTime);
        var registrations = new List<PlannedTrigger>();
        var schedules = new List<PlannedSchedule>();

        foreach (BaseNode node in definition.Nodes)
        {
            // At most one channel claims a node: the one its trigger kind (and, for a client trigger,
            // whether it holds) belongs to.
            foreach (ITriggerChannel channel in _channels.All)
            {
                TriggerRegistrationSpec? spec = channel.Plan(node, publication);
                if (spec is null)
                {
                    continue;
                }

                registrations.Add(new PlannedTrigger(
                    node.NodeId,
                    channel.Kind,
                    spec.TriggerKey,
                    spec.CorrelationExpression,
                    TriggerRegistrationRow.FormatConcurrencyPolicy(spec.ConcurrencyPolicy),

                    // Copied onto the row rather than read from the definition at dispatch time: the dispatcher
                    // matches registrations by key and would otherwise have to load and deserialize a whole
                    // definition per candidate just to decide whether to discard the event.
                    spec.Filter is null ? null : JsonSerializer.Serialize(spec.Filter, EngineJson.Options),
                    spec.WebhookSecret));

                if (spec.Schedule is not null)
                {
                    schedules.Add(new PlannedSchedule(node.NodeId, spec.Schedule.CronOrInterval, spec.Schedule.NextFireAt));
                }

                break;
            }
        }

        return new TriggerPlan(registrations, schedules);
    }
}
