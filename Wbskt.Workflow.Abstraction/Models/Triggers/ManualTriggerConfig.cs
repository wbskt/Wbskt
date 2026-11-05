using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ManualTriggerConfig
{
    [JsonPropertyName("description")]
    public string? Description { get; init; } = null;

    /// <summary>
    /// What a run started by hand asks for, such as <c>client</c> and <c>reason</c>: a client shows a
    /// small form from these before Run now. Each becomes a key of the run's body
    /// (<c>$trigger.body.NAME</c>); one the caller leaves out takes its <see cref="ManualTriggerInput.Default"/>.
    /// Names are unique, and may not be one the engine writes itself (<c>parentRunRefId</c>,
    /// <c>correlationKey</c>, <c>depth</c>).
    /// </summary>
    [JsonPropertyName("inputs")]
    public IReadOnlyList<ManualTriggerInput>? Inputs { get; init; } = null;
}

/// <summary>One input a manual trigger declares.</summary>
public sealed record ManualTriggerInput
{
    /// <summary>The body key: a letter or underscore, then letters, digits or underscores.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>What the form shows; the name when unset.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; } = null;

    /// <summary>Any JSON value, used when the caller sends no value for this input.</summary>
    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; } = null;
}
