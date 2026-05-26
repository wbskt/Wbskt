namespace Wbskt.Workflow.Abstraction.Validation;

public sealed record ValidationIssue(
    Wbskt.Workflow.Abstraction.Enums.ValidationSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null
);
