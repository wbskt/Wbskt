using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services.Workflow;

internal static class WorkflowDraftErrors
{
    public static readonly Error NotFound = Error.NotFound("WORKFLOW_DRAFT_NOT_FOUND", "The workflow has no draft.");

    public static readonly Error Conflict = Error.Conflict(
        "WORKFLOW_DRAFT_CONFLICT",
        "The draft was changed, discarded or published since it was read. Load it again.");

    public static readonly Error Outdated = Error.Conflict(
        "WORKFLOW_DRAFT_OUTDATED",
        "Another version was published since this draft was started. Publish anyway to replace it.");

    public static readonly Error RefTaken = Error.Conflict("WORKFLOW_REF_TAKEN", "This workflow reference is already in use. Draft under a new RefId.");

    public static readonly Error Deleted = Error.Conflict("WORKFLOW_DELETED", "The workflow was deleted and cannot be drafted again.");
}
