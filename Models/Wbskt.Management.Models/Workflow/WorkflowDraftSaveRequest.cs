using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Saves a draft. <c>ExpectedRevision</c> is the revision last read, or null to start a draft; a draft
/// changed since is refused with 409 <c>WORKFLOW_DRAFT_CONFLICT</c>. An invalid definition is still saved.
/// </summary>
public record WorkflowDraftSaveRequest(string Name, string? Description, WorkflowDefinition Definition, int? ExpectedRevision = null);
