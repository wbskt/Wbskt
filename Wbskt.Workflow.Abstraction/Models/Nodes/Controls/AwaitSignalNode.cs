using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record AwaitSignalNode : BaseNode, ITemplatedStep
{
    [JsonPropertyName("config")]
    public AwaitSignalConfig? Config { get; init; }

    [JsonIgnore]
    object? ITemplatedStep.TemplatedConfig => Config;

    [JsonIgnore]
    public override string Kind => NodeKind.ControlAwaitSignal;
}
