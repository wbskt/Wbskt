using System.Text.Json.Serialization;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Publishes a draft at <c>Revision</c>. When another version was published since the draft was started,
/// the publish is refused with 409 <c>WORKFLOW_DRAFT_OUTDATED</c> unless <c>Force</c> is true.
/// <c>Enable</c> is required: true makes the new version live as it is published; false leaves the live
/// version (or a disabled workflow) as it is. <c>Note</c> is what the author says about this version, shown
/// beside it in the version list; at most 500 characters, and blank is none.
/// </summary>
public record WorkflowDraftPublishRequest(int Revision, [property: JsonRequired] bool Enable, bool Force = false, string? Note = null);
