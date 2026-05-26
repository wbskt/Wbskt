namespace Wbskt.Management.Models.Workflow;

public record SharedVariableDto(Guid WorkflowRefId, string VarName, string VarType, string ValueJson, DateTime UpdatedAt);