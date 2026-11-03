using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Channels;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// The names the workflow list uses for what starts a workflow: the trigger channel names
/// <c>TriggerRegistrationService</c> registers, except that a held client trigger reads as <c>client</c>.
/// </summary>
internal static class WorkflowTriggerKinds
{
    public static IReadOnlyList<string> FromNodeKinds(IReadOnlyList<string>? nodeKinds) =>
        (nodeKinds ?? []).Select(FromNodeKind).Distinct(StringComparer.Ordinal).ToList();

    private static string FromNodeKind(string nodeKind) => nodeKind switch
    {
        NodeKind.TriggerClient => ClientChannel.Name,
        NodeKind.TriggerClientPresence => PresenceChannel.Name,
        NodeKind.TriggerSchedule => ScheduleChannel.Name,
        NodeKind.TriggerWebhook => WebhookChannel.Name,
        NodeKind.TriggerManual => ManualChannel.Name,
        // A trigger kind newer than this list still says what it is.
        _ => nodeKind.StartsWith("trigger:", StringComparison.Ordinal) ? nodeKind["trigger:".Length..] : nodeKind
    };
}
