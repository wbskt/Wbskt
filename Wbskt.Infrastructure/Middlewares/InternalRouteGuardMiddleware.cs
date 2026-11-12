using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Wbskt.Infrastructure.Middlewares;

/// <summary>
/// The host's own wall around <see cref="InternalRoutes.Prefix"/>: a request for it that came through
/// a proxy is answered 404, as if the route did not exist, and logged. Host-to-host calls go straight to
/// the container (<c>http://auth:8080</c>), so they never carry a forwarded header, while Traefik adds
/// <c>X-Forwarded-For</c> to everything it passes on. The public routers exclude the prefix too
/// (deploy/compose/docker-compose.yml); this holds if a router rule ever misses a spelling of it.
/// </summary>
/// <remarks>
/// <para>
/// A request is internal when its path starts with the prefix in any letter case, or when routing has
/// already picked an internal endpoint for it (every host routes before <c>UseWbsktPipeline</c>). Kestrel
/// decodes and normalises the path first, so <c>/api/%69nternal</c> and <c>/api/./internal</c> are seen
/// as the prefix; the endpoint check is defence in depth for a path routing matches and the prefix
/// check does not.
/// </para>
/// <para>
/// Runs before <c>UseForwardedHeaders</c>, which removes the forwarded values it applies, and before the
/// exception handler and the rate limiter, which a fixed 404 needs neither of.
/// </para>
/// <para>
/// A host that calls another through Traefik's backend entrypoint (management → engine on
/// <c>http://traefik:8081</c>) sends forwarded headers too, so an internal route on such a host would be
/// refused. None is today; one that is added (#787) must be called directly instead.
/// </para>
/// </remarks>
public sealed class InternalRouteGuardMiddleware
{
    private static readonly PathString Internal = "/" + InternalRoutes.Prefix;

    private readonly RequestDelegate _next;
    private readonly ILogger<InternalRouteGuardMiddleware> _logger;

    public InternalRouteGuardMiddleware(RequestDelegate next, ILogger<InternalRouteGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (IsProxiedInternalRequest(context))
        {
            string forwardedFor = context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName].ToString();
            _logger.LogWarning(
                "Refused {Path} from {IpAddress}: an internal route reached through a proxy, possibly a public router rule that no longer excludes it.",
                context.Request.Path.Value, forwardedFor.Length > 100 ? forwardedFor[..100] : forwardedFor);
            return Problems.WriteAsync(context, StatusCodes.Status404NotFound, Problems.CodeFor(StatusCodes.Status404NotFound), detail: null);
        }

        return _next(context);
    }

    /// <summary>Whether the request in <paramref name="context"/> is for an internal route and arrived through a proxy.</summary>
    public static bool IsProxiedInternalRequest(HttpContext context) =>
        IsInternal(context) && (context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName)
            || context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedHostHeaderName));

    private static bool IsInternal(HttpContext context) =>
        context.Request.Path.StartsWithSegments(Internal, StringComparison.OrdinalIgnoreCase)
        || (context.GetEndpoint() is RouteEndpoint endpoint
            && endpoint.RoutePattern.RawText is { } pattern
            && new PathString("/" + pattern.TrimStart('/')).StartsWithSegments(Internal, StringComparison.OrdinalIgnoreCase));
}
