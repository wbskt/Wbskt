namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>That a published workflow has a draft, as the workflow list reports it.</summary>
public sealed record WorkflowDraftSummary(int Revision, int? BaseVersion, Guid? UpdatedByRefId, DateTime UpdatedAt);
