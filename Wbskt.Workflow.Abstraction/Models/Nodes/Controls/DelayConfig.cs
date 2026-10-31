using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record DelayConfig
{
    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; init; }

}

