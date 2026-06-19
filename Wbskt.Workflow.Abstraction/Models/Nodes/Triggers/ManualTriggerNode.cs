using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed record ManualTriggerNode : BaseNode
{
    [JsonPropertyName("config")]
    public required ManualTriggerConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.TriggerManual;
}
