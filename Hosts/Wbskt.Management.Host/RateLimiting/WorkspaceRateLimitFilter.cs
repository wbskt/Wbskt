using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc.Filters;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.RateLimiting;

/// <summary>
/// Spends a permit of the action's <see cref="WorkspaceRateLimitAttribute"/> budget, and answers 429
/// RATE_LIMITED with <c>Retry-After</c> once it is gone. An action filter, so it runs after
/// authentication and after <c>WorkspacePermissionFilter</c> has let the caller through.
/// </summary>
public sealed class WorkspaceRateLimitFilter(WorkspaceRateLimiter limiter) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        WorkspaceRateLimitAttribute? limit = context.ActionDescriptor.EndpointMetadata.OfType<WorkspaceRateLimitAttribute>().FirstOrDefault();

        // Normalised: the route accepts any GUID spelling, and each raw spelling would otherwise get a window of its own.
        if (limit is null || !Guid.TryParse(context.RouteData.Values["workspaceRef"]?.ToString(), out Guid workspaceRef))
        {
            await next();
            return;
        }

        using RateLimitLease lease = limiter.Acquire(limit.Policy, workspaceRef);
        if (!lease.IsAcquired)
        {
            int retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : 1;
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            context.Result = new ApiErrorResults.ProblemResult(
                Error.Unavailable("RATE_LIMITED", "Too many requests. Slow down and try again shortly."),
                StatusCodes.Status429TooManyRequests);
            return;
        }

        await next();
    }
}
