using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record AwaitSignalConfig
{
    [JsonPropertyName("signalName")]
    public string SignalName { get; init; } = string.Empty;

    /// <summary>
    /// What the signal must be sent to. Left out, the run's own RefId. Placeholders such as
    /// <c>{{trigger.payload.parentRun}}</c> are filled in from the branch when the step parks.
    /// </summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("correlation")]
    public string? Correlation { get; init; } = null;

    [JsonPropertyName("ttl")]
    public TimeSpan? Ttl { get; init; } = null;

}
