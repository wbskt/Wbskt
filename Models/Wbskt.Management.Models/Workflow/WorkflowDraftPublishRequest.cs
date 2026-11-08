namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Publishes a draft at <c>Revision</c>. When another version was published since the draft was started,
/// the publish is refused with 409 <c>WORKFLOW_DRAFT_OUTDATED</c> unless <c>Force</c> is true.
/// </summary>
public record WorkflowDraftPublishRequest(int Revision, bool Force = false);
