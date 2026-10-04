using System.Threading.RateLimiting;

namespace Wbskt.Management.Host.Controllers.Workflow;

/// <summary>
/// Shared limits for the anonymous public callback edge (WaitForHttp wakes and webhook triggers).
/// These endpoints are gated only by possession of a token/path, so they are throttled and their
/// request bodies are capped to bound how much attacker-controlled data can be persisted into
/// workflow state per call.
/// </summary>
internal static class PublicCallbackPolicy
{
    /// <summary>Named rate-limiting policy applied to the public callback controller.</summary>
    public const string RateLimitPolicy = "public-callbacks";

    /// <summary>The routes the per-IP flood ceiling covers.</summary>
    public static readonly PathString CallbackRoutes = "/api/callbacks";

    /// <summary>Maximum accepted request body size (128 KiB) for a callback payload.</summary>
    public const long MaxBodyBytes = 128 * 1024;
}

/// <summary>
/// How the public callbacks are throttled. A webhook's budget belongs to its target, the workspace
/// and path, not to the address it comes from: LoRaWAN network servers (The Things Network,
/// ChirpStack) and vendor clouds forward every device's uplink from a few shared addresses, so a
/// per-IP budget turned one busy fleet, or two unrelated workspaces behind the same forwarder, into
/// lost readings. The address keeps a much higher ceiling across every callback, as flood protection
/// and to bound how fast one caller can try paths. A wake token names one parked run, so wakes stay
/// limited per address.
/// </summary>
/// <remarks>
/// Configured under <c>RateLimiting:Webhook</c> (per workspace and path), <c>RateLimiting:Wake</c>
/// (per address) and <c>RateLimiting:PublicCallbacks</c> (the per-address ceiling), each with
/// <c>PermitLimit</c> and <c>WindowSeconds</c>.
/// </remarks>
internal sealed class PublicCallbackRateLimits
{
    private readonly FixedWindowRateLimiterOptions _webhook;
    private readonly FixedWindowRateLimiterOptions _wake;
    private readonly FixedWindowRateLimiterOptions _perAddress;

    public PublicCallbackRateLimits(IConfiguration configuration)
    {
        _webhook = Window(configuration.GetSection("RateLimiting:Webhook"), permitLimit: 600);
        _wake = Window(configuration.GetSection("RateLimiting:Wake"), permitLimit: 60);
        _perAddress = Window(configuration.GetSection("RateLimiting:PublicCallbacks"), permitLimit: 1200);
    }

    /// <summary>The endpoint policy: a webhook by its workspace and path, a wake by the caller's address.</summary>
    public RateLimitPartition<string> ForTarget(HttpContext httpContext)
    {
        var routeValues = httpContext.Request.RouteValues;
        if (Guid.TryParse(routeValues.GetValueOrDefault("workspaceRef")?.ToString(), out var workspaceRef)
            && routeValues.GetValueOrDefault("path")?.ToString() is { } path)
        {
            // Normalised, so another spelling of the same target is not a fresh budget: the route
            // accepts a GUID in any of its formats, and paths are compared without regard to case.
            var key = $"webhook:{workspaceRef:D}:{path.ToLowerInvariant()}";
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => _webhook);
        }

        return RateLimitPartition.GetFixedWindowLimiter($"wake:{Address(httpContext)}", _ => _wake);
    }

    /// <summary>The global ceiling: every callback from one address, whatever it targets. Other routes are not limited here.</summary>
    public RateLimitPartition<string> ForAddress(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments(PublicCallbackPolicy.CallbackRoutes))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        return RateLimitPartition.GetFixedWindowLimiter($"callbacks:{Address(httpContext)}", _ => _perAddress);
    }

    private static string Address(HttpContext httpContext) => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(IConfiguration section, int permitLimit) => new()
    {
        PermitLimit = section.GetValue("PermitLimit", permitLimit),
        Window = TimeSpan.FromSeconds(section.GetValue("WindowSeconds", 60)),
        QueueLimit = 0
    };
}
