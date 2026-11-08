namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// One workflow in the list, at its newest version. <paramref name="TriggerKinds"/> says what can start it:
/// <c>client</c> (a client message, held or not), <c>presence</c>, <c>schedule</c>, <c>webhook</c> and
/// <c>manual</c>, distinct and in node order, so a client can show an icon and offer Run now only when
/// <c>manual</c> is there. <paramref name="Draft"/> is set when the workflow has unpublished changes; a
/// workflow never published is not listed here but in <c>GET workflows/drafts</c>.
/// </summary>
public record WorkflowSummaryDto(Guid RefId, int Version, string Status, string Name, string? Description, DateTimeOffset CreatedAt, IReadOnlyList<string>? TriggerKinds = null, WorkflowDraftInfoDto? Draft = null);
