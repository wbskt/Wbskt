using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure.Middlewares;

/// <summary>
/// The budget for the signed-in API: a token bucket per user, read from
/// <c>RateLimiting:Api:PermitsPerMinute</c> (refill) and <c>RateLimiting:Api:Burst</c> (bucket size).
/// </summary>
/// <remarks>
/// Partitioned on the user, not the client address, so colleagues behind one NAT do not share a budget,
/// and one tenant's script or leaked token is throttled without touching anyone else. Not on the tenant:
/// a user token names only its user (#306), and the tenant a request acts in is known only once its
/// workspace is resolved, which is after this. Device tokens and anonymous calls are not counted here:
/// devices have their own limits on the socket host and the device endpoints, and a fleet reconnecting
/// after a restart is legitimate traffic; the anonymous edges have their per-IP policies.
/// </remarks>
public sealed class UserRateLimiter : IDisposable
{
    public const int DefaultPermitsPerMinute = 600;
    public const int DefaultBurst = 120;

    private readonly PartitionedRateLimiter<Guid> _limiter;

    public UserRateLimiter(int permitsPerMinute, int burst)
    {
        // Refilled every second rather than once a minute, so a user who spends the burst waits
        // seconds, not the rest of the minute.
        int perSecond = Math.Max(1, (int)Math.Ceiling(permitsPerMinute / 60d));
        _limiter = PartitionedRateLimiter.Create<Guid, Guid>(userRef =>
            RateLimitPartition.GetTokenBucketLimiter(userRef, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, burst),
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public static UserRateLimiter FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue("RateLimiting:Api:PermitsPerMinute", DefaultPermitsPerMinute),
        configuration.GetValue("RateLimiting:Api:Burst", DefaultBurst));

    public RateLimitLease Acquire(Guid userRef) => _limiter.AttemptAcquire(userRef);

    public void Dispose() => _limiter.Dispose();
}

/// <summary>
/// Spends one permit of the caller's <see cref="UserRateLimiter"/> budget per request, and answers 429
/// RATE_LIMITED with <c>Retry-After</c> once it is gone. Runs after authentication, since the
/// partition is the user the token names; the anonymous throttles run before it, in UseRateLimiter.
/// </summary>
public sealed class UserRateLimitMiddleware(RequestDelegate next, UserRateLimiter limiter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!WbsktClaims.TryReadUser(context.User, out Guid userRef, out _))
        {
            await next(context);
            return;
        }

        using RateLimitLease lease = limiter.Acquire(userRef);
        if (!lease.IsAcquired)
        {
            int retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : 1;
            context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            await Problems.WriteAsync(context, StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many requests. Slow down and try again shortly.");
            return;
        }

        await next(context);
    }
}
