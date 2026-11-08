namespace Wbskt.Management.Models.Workflow;

/// <summary>A draft in a list, without its definition. <c>UpdatedBy</c> is the user's RefId.</summary>
public record WorkflowDraftSummaryDto(
    Guid RefId,
    int Revision,
    int? BaseVersion,
    int? CurrentVersion,
    bool Outdated,
    string Name,
    string? Description,
    Guid? UpdatedBy,
    DateTimeOffset UpdatedAt);
