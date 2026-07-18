using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record WaitForHttpConfig
{
    [JsonPropertyName("ttl")]
    public required TimeSpan Ttl { get; init; }

}

