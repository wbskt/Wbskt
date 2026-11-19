namespace Wbskt.Management.Host.Models;

/// <summary>A client as an ownership check sees it: its id and the workspace it belongs to.</summary>
public sealed record ClientOwner(int Id, int WorkspaceId) : IWorkspaceOwned;
