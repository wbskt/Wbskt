using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A child run reaching a terminal status. Only wakes the parent branch parked on its sub-workflow
/// node; nothing registers a trigger on it. Keys: <c>child-completed:{childRunRef}</c>.
/// </summary>
public sealed class ChildCompletedChannel : TriggerChannel
{
    public const string Name = "child-completed";

    public override string Kind => Name;

    public static string Key(string childRunRef) => $"{Name}:{childRunRef}";

    public static InboundEvent Event(Guid childRunRef, string terminalStatus, Guid eventId) => new(
        Name,
        [Key(childRunRef.ToString())],
        $"{Name}:{childRunRef}:{eventId}",
        new Dictionary<string, JsonElement>
        {
            ["childRunRefId"] = Json(childRunRef),
            ["status"] = Json(terminalStatus)
        },
        default);

    public override string CorrelationKey(InboundEvent evt) => Key(Required(evt, "childRunRefId"));
}
