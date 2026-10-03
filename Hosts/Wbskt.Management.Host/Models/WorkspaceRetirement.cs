namespace Wbskt.Management.Host.Models;

/// <summary>What <c>dbo.Workspace_Retire</c> left for the caller to act on outside the database.</summary>
public sealed record WorkspaceRetirement(
    IReadOnlyList<RetiredClient> RevokedClients,
    IReadOnlyList<long> ActiveRunIds,
    IReadOnlyList<int> DefinitionIds);

public sealed record RetiredClient(int ClientId, Guid ClientRefId, int PolicyId, Guid PolicyRefId);
