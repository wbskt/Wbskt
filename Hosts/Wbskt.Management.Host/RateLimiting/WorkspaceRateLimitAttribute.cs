namespace Wbskt.Management.Host.RateLimiting;

/// <summary>
/// Holds the action to a budget per workspace (<see cref="WorkspaceRateLimiter"/>), spent only by a caller
/// who passed authentication and the action's permission check, so someone who merely knows a workspace's
/// reference cannot use its budget up.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WorkspaceRateLimitAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}
