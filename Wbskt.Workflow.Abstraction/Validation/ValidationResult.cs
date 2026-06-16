namespace Wbskt.Workflow.Abstraction.Validation;

public sealed record ValidationResult(IReadOnlyCollection<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != Enums.ValidationSeverity.Error);
}
