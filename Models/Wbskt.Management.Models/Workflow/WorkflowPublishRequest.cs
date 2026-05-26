using System.Text.Json;

namespace Wbskt.Management.Models.Workflow;

public record WorkflowPublishRequest(Guid RefId, string Name, string? Description, JsonElement Definition);