namespace Wbskt.Workflow.Abstraction.Entities;

public record SharedVariableRow
{
    public required int Id { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required string VarName { get; init; }
    public required string VarType { get; init; }
    public required string ValueJson { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
}
