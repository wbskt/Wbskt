namespace Wbskt.Workflow.Abstraction.Validation;

public sealed record ValidationIssue(
    Enums.ValidationSeverity Severity,
    string Code,
    string Message,
    Guid? NodeId = null
);
