namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// One thing the validator found. <c>Severity</c> is "Error" or "Warning"; only errors block a
/// publish, but warnings are returned too so an author can see them without guessing.
/// </summary>
public record WorkflowValidationIssueDto(string Severity, string Code, string Message, Guid? NodeId);

/// <summary>
/// The result of validating a definition. A definition with issues is still a successful
/// <i>response</i> - the request was answered, the answer is "here is what is wrong" - so this is
/// returned with 200 and <c>IsValid</c> false, not as an HTTP error.
/// </summary>
public record WorkflowValidationResponse(bool IsValid, IReadOnlyCollection<WorkflowValidationIssueDto> Issues);
