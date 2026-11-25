namespace Wbskt.Management.Host.Services.Caching;

/// <summary>
/// The one place a shared cache key is built. Every key starts with the workspace id that
/// <c>WorkspacePermissionFilter</c> resolved, so an entry is reached only by a caller already allowed
/// in that workspace, and none holds anything about the caller.
/// </summary>
internal static class WorkspaceCacheKeys
{
    public static string RunStats(int workspaceId, DateTime fromUtc, DateTime toUtc) =>
        $"ws:{workspaceId}:run-stats:{fromUtc.Ticks}:{toUtc.Ticks}";

    public static string WorkflowRunStats(int workspaceId, Guid workflowRefId, DateTime fromUtc, DateTime toUtc, bool withSlowestNodes) =>
        $"ws:{workspaceId}:workflow:{workflowRefId:N}:run-stats:{fromUtc.Ticks}:{toUtc.Ticks}:{(withSlowestNodes ? "nodes" : "plain")}";
}
