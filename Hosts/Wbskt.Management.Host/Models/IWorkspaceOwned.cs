namespace Wbskt.Management.Host.Models;

/// <summary>A resource that belongs to exactly one workspace.</summary>
public interface IWorkspaceOwned
{
    int WorkspaceId { get; }
}
