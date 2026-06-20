using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record ForkConfig
{
    [JsonPropertyName("branches")]
    public required IReadOnlyCollection<string> Branches { get; init; }

}

