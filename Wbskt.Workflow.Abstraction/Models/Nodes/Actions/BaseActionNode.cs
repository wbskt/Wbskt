using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public abstract record BaseActionNode(
    Guid NodeId,
    string Name,
    IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("retry")] RetryPolicy? Retry,
    [property: JsonPropertyName("onFailure")] OnFailureConfig? OnFailure
) : BaseNode(NodeId, Name, Ports);
