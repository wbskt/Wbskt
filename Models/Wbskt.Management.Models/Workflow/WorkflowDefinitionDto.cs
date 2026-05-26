using System.Text.Json;

namespace Wbskt.Management.Models.Workflow;

public record WorkflowDefinitionDto(Guid RefId, int Version, string Status, string Name, string? Description, JsonElement Definition, DateTime CreatedAt);