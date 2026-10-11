using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A named signal sent to a run. Only wakes branches parked on an Await Signal node; nothing registers
/// a trigger on it. Keys: <c>signal:{name}:{scope}</c>, where scope is the run the signal is for.
/// </summary>
public sealed class SignalChannel : TriggerChannel
{
    public const string Name = "signal";

    public override string Kind => Name;

    public static string Key(string signalName, string scope) => $"{Name}:{signalName}:{scope}";

    public static InboundEvent Event(Guid scopeRunRefId, string signalName, JsonElement body) => new(
        Name,
        [Key(signalName, scopeRunRefId.ToString())],
        $"{Name}:{scopeRunRefId}:{signalName}:{Guid.NewGuid()}",
        new Dictionary<string, JsonElement>
        {
            ["signalName"] = Json(signalName),
            ["scopeRunRefId"] = Json(scopeRunRefId.ToString()),
            ["body"] = body
        },
        default);

    public override string CorrelationKey(InboundEvent evt) => Key(Required(evt, "signalName"), Required(evt, "scopeRunRefId"));
}
