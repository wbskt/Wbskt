using Microsoft.AspNetCore.Http;

namespace Wbskt.Infrastructure.Middlewares;

/// <summary>
/// The host's own wall around <see cref="InternalRoutes.Prefix"/>: a request for it that came through
/// a proxy is answered 404, as if the route did not exist. Host-to-host calls go straight to the
/// container (<c>http://auth:8080</c>), so they never carry a forwarded header, while Traefik adds
/// <c>X-Forwarded-For</c> to everything it passes on. The public routers exclude the prefix too
/// (deploy/compose/docker-compose.yml); this holds if a router rule ever misses a spelling of it.
/// </summary>
/// <remarks>
/// Runs before <c>UseForwardedHeaders</c>, which removes the forwarded values it applies. The path is
/// compared ignoring case because routing matches it that way.
/// </remarks>
public sealed class InternalRouteGuardMiddleware
{
    private static readonly PathString Internal = "/" + InternalRoutes.Prefix;

    private readonly RequestDelegate _next;

    public InternalRouteGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (IsProxiedInternalRequest(context.Request))
        {
            return Problems.WriteAsync(context, StatusCodes.Status404NotFound, Problems.CodeFor(StatusCodes.Status404NotFound), detail: null);
        }

        return _next(context);
    }

    /// <summary>Whether <paramref name="request"/> is for an internal route and arrived through a proxy.</summary>
    public static bool IsProxiedInternalRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(Internal, StringComparison.OrdinalIgnoreCase)
        && (request.Headers.ContainsKey("X-Forwarded-For") || request.Headers.ContainsKey("X-Forwarded-Host"));
}
