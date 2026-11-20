namespace Wbskt.Management.Models;

/// <summary>What a workspace holds, for its card on the workspaces list and the Overview.</summary>
public sealed record WorkspaceCountsResponse(ClientStateCounts Clients, int Policies, WorkflowStateCounts Workflows);

/// <summary>
/// The workspace's clients by state: <c>Online</c> and <c>Offline</c> are registered clients connected or not,
/// and <c>Total</c> counts every client, whatever its state.
/// </summary>
public sealed record ClientStateCounts(int Total, int Online, int Offline, int Pending, int Revoked, int Rejected);

/// <summary>The workspace's workflows by state; a deleted workflow is not counted.</summary>
public sealed record WorkflowStateCounts(int Total, int Enabled, int Disabled, int Deprecated);
