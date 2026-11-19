namespace Wbskt.Management.Host.Models;

/// <summary>What <c>dbo.Workspace_Freeze</c> left for the caller to act on outside the database.</summary>
public sealed record WorkspaceFreeze(IReadOnlyList<RetiredClient> RevokedClients, IReadOnlyList<long> ActiveRunIds);
