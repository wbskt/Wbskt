using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public abstract record BaseActionNode : BaseNode
{


    [JsonPropertyName("retry")]
    public RetryPolicy? Retry { get; init; }

    [JsonPropertyName("onFailure")]
    public OnFailureConfig? OnFailure { get; init; }

    [JsonPropertyName("compensation")]
    public CompensationDeclaration? Compensation { get; init; }
}
