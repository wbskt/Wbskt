using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>A run list's one-line account of what started a run, from its trigger's settings.</summary>
internal static class RunTriggerSummaries
{
    public static string? For(BaseNode? trigger, Guid? parentRunRefId)
    {
        if (parentRunRefId is not null)
        {
            return "Started by another workflow's run";
        }

        return trigger switch
        {
            ClientTriggerNode { Config: { } c } => c.Type == "*" ? $"Device {c.ClientRef} sent a message" : $"Device {c.ClientRef} sent {c.Type}",
            ClientPresenceTriggerNode { Config: { } c } => $"Device {c.ClientRef} {c.State.ToString().ToLowerInvariant()} for {c.ForSeconds} s",
            ScheduleTriggerNode { Config: { } c } => $"Schedule {c.Cron}",
            WebhookTriggerNode { Config: { } c } => $"Webhook /{c.Path.TrimStart('/')}",
            ManualTriggerNode => "Started by hand",
            null => null,
            _ => trigger.Name
        };
    }
}
