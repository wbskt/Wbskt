using System.Threading.RateLimiting;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.RateLimiting;

/// <summary>
/// The per-workspace budgets of <see cref="WorkspaceRateLimitAttribute"/>, a fixed window each, read from
/// <c>RateLimiting:{name}:PermitLimit</c> and <c>RateLimiting:{name}:WindowMinutes</c>.
/// </summary>
public sealed class WorkspaceRateLimiter : IDisposable
{
    private readonly Dictionary<string, PartitionedRateLimiter<Guid>> _limiters;

    public WorkspaceRateLimiter(IConfiguration configuration)
    {
        _limiters = new Dictionary<string, PartitionedRateLimiter<Guid>>(StringComparer.Ordinal)
        {
            // Generous for someone setting an integration up, a wall for a loop.
            [RateLimitPolicies.IntegrationTest] = Create(configuration, "IntegrationTest", defaultPermitLimit: 20),

            // Enough for someone clicking through filters, a wall for a loop.
            [RateLimitPolicies.Export] = Create(configuration, "Export", defaultPermitLimit: 10)
        };
    }

    /// <summary>A permit from <paramref name="policy"/>'s budget for the workspace; throws for a policy it does not know.</summary>
    public RateLimitLease Acquire(string policy, Guid workspaceRef)
    {
        if (!_limiters.TryGetValue(policy, out PartitionedRateLimiter<Guid>? limiter))
        {
            throw new InvalidOperationException($"No workspace rate limit is configured for '{policy}'.");
        }

        return limiter.AttemptAcquire(workspaceRef);
    }

    public void Dispose()
    {
        foreach (PartitionedRateLimiter<Guid> limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }

    private static PartitionedRateLimiter<Guid> Create(IConfiguration configuration, string name, int defaultPermitLimit)
    {
        int permitLimit = configuration.GetValue($"RateLimiting:{name}:PermitLimit", defaultPermitLimit);
        var window = TimeSpan.FromMinutes(configuration.GetValue($"RateLimiting:{name}:WindowMinutes", 1));
        return PartitionedRateLimiter.Create<Guid, Guid>(workspaceRef =>
            RateLimitPartition.GetFixedWindowLimiter(workspaceRef, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0
            }));
    }
}
