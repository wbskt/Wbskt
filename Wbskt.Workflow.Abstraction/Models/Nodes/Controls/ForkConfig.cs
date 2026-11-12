using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record ForkConfig
{
    // Empty rather than required: the validator lists it as FORK_BRANCHES_MISSING.
    [JsonPropertyName("branches")]
    public IReadOnlyCollection<string> Branches { get; init; } = [];

}

