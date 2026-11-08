namespace Wbskt.Workflow.Abstraction.Entities;

public sealed record WorkflowDraftSave(
    Guid RefId,
    int WorkspaceId,
    string Name,
    string? Description,
    string DefinitionJson,
    int UserId,
    Guid? UserRefId);
