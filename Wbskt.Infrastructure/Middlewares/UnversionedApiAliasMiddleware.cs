using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Wbskt.Infrastructure.Middlewares;

/// <summary>
/// Serves the pre-<c>/v1</c> paths for one release: <c>/api/clients…</c> is handled as
/// <c>/api/v1/clients…</c> and answered with <c>Deprecation: true</c>, so a console or device that
/// hasn't moved yet keeps working. Paths in <see cref="PublicApi.Unversioned"/> pass through untouched.
/// Must run before routing; see <see cref="UnversionedApiAliasExtensions.UseUnversionedApiAliases"/>.
/// </summary>
public sealed class UnversionedApiAliasMiddleware
{
    private static readonly PathString Api = "/api";
    private static readonly PathString Versioned = "/" + PublicApi.Prefix;

    private readonly RequestDelegate _next;

    public UnversionedApiAliasMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (TryAlias(context.Request.Path, out var versioned))
        {
            context.Request.Path = versioned;
            context.Response.Headers["Deprecation"] = "true";
        }

        return _next(context);
    }

    /// <summary>The versioned path for <paramref name="path"/>, when it is an unversioned public API path.</summary>
    public static bool TryAlias(PathString path, out PathString versioned)
    {
        versioned = path;
        if (!path.StartsWithSegments(Api, StringComparison.Ordinal, out var rest) || !rest.HasValue)
        {
            return false;
        }

        string remainder = rest.Value!;
        int end = remainder.IndexOf('/', 1);
        string first = end < 0 ? remainder[1..] : remainder[1..end];
        if (first.Length == 0 || PublicApi.Unversioned.Contains(first))
        {
            return false;
        }

        versioned = Versioned.Add(rest);
        return true;
    }
}

public static class UnversionedApiAliasExtensions
{
    /// <summary>
    /// Adds <see cref="UnversionedApiAliasMiddleware"/> and then routing, explicitly: without the
    /// explicit <c>UseRouting</c> a minimal host routes before any middleware, and the rewritten path
    /// would never be matched. Call it before <c>UseWbsktPipeline</c>.
    /// </summary>
    public static WebApplication UseUnversionedApiAliases(this WebApplication app)
    {
        app.UseMiddleware<UnversionedApiAliasMiddleware>();
        app.UseRouting();
        return app;
    }
}
