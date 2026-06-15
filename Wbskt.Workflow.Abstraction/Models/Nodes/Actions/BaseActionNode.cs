using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public abstract record BaseActionNode(
    Guid NodeId,
    string Name,
    IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("retry")] RetryPolicy? Retry,
    [property: JsonPropertyName("onFailure")] OnFailureConfig? OnFailure,
    [property: JsonPropertyName("compensation")] CompensationDeclaration? Compensation = null
) : BaseNode(NodeId, Name, Ports);
