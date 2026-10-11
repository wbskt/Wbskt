using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

/// <summary>Wakes when the device acknowledges the command, or the platform reports it failed.</summary>
public sealed record CommandOutcomeWakeCondition(
    [property: JsonPropertyName("commandId")] Guid CommandId
) : WakeCondition;
