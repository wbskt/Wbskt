using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// A workflow's draft. <c>BaseVersion</c> is the version it was started from (null for a workflow never
/// published) and <c>CurrentVersion</c> the live one now; when they differ the draft is <c>Outdated</c>
/// and publishing it needs <c>force</c>. Save again with <c>Revision</c> as <c>expectedRevision</c>.
/// <c>Validation</c> is what publishing it would say now.
/// </summary>
public record WorkflowDraftDto(
    Guid RefId,
    int Revision,
    int? BaseVersion,
    int? CurrentVersion,
    bool Outdated,
    string Name,
    string? Description,
    WorkflowDefinition Definition,
    Guid? UpdatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt,
    WorkflowValidationResponse Validation);
