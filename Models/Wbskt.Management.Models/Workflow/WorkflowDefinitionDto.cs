using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

public record WorkflowDefinitionDto(Guid RefId, int Version, string Status, string Name, string? Description, WorkflowDefinition Definition, DateTime CreatedAt);