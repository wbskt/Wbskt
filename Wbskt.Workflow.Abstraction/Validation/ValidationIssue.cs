namespace Wbskt.Workflow.Abstraction.Validation;

/// <param name="Field">
/// The setting at fault, as a camel-case path into the node (<c>config.chatId</c>,
/// <c>config.embeds[0].title</c>), so the designer can point at the input and not only the step.
/// Null for a problem with the step or the graph as a whole.
/// </param>
public sealed record ValidationIssue(
    Enums.ValidationSeverity Severity,
    string Code,
    string Message,
    Guid? NodeId = null,
    string? Field = null
);
