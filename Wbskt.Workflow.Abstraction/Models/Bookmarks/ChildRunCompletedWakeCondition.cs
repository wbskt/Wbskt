using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record ChildRunCompletedWakeCondition(
    [property: JsonPropertyName("childRunId")] Guid ChildRunId
) : WakeCondition;
