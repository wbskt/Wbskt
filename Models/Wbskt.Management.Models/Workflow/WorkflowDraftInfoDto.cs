namespace Wbskt.Management.Models.Workflow;

/// <summary>That a published workflow has a draft, on its row in the workflow list.</summary>
public record WorkflowDraftInfoDto(int Revision, int? BaseVersion, Guid? UpdatedBy, DateTimeOffset UpdatedAt);
