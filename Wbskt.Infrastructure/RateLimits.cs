using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Infrastructure;

public static class RateLimits
{
    /// <summary>
    /// A fixed window per client IP, read from <c>RateLimiting:{section}:PermitLimit</c> and
    /// <c>:WindowMinutes</c> (one minute by default). The IP is the real client's once the host's
    /// pipeline has applied the forwarded headers (see <see cref="Hosting.WbsktHost.UseWbsktPipeline"/>).
    /// </summary>
    public static RateLimiterOptions AddPerIpPolicy(this RateLimiterOptions options, IConfiguration configuration, string policy, string section, int defaultPermitLimit)
    {
        int permitLimit = configuration.GetValue($"RateLimiting:{section}:PermitLimit", defaultPermitLimit);
        var window = TimeSpan.FromMinutes(configuration.GetValue($"RateLimiting:{section}:WindowMinutes", 1));

        return options.AddPolicy(policy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0
                }));
    }

    /// <summary>
    /// Counts every signed-in user's requests against a <see cref="UserRateLimiter"/>, which
    /// <see cref="Hosting.WbsktHost.UseWbsktPipeline"/> applies after authentication.
    /// </summary>
    public static IServiceCollection AddUserRateLimit(this IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton(_ => UserRateLimiter.FromConfiguration(configuration));
}
