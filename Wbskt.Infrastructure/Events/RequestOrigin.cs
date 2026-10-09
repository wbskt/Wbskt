using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure.Configuration;

namespace Wbskt.Infrastructure.Events;

/// <summary>Where the current request came from: the console or another API caller, and its address and user agent.</summary>
public sealed record RequestOrigin(EventSource Source, string? ClientAddress, string? UserAgent);

/// <summary>Reads <see cref="RequestOrigin"/> for the request in scope, if there is one.</summary>
public interface IRequestOriginAccessor
{
    RequestOrigin? Current { get; }
}

/// <summary>
/// A request is from the console when its <c>Origin</c> header is one the host lets browsers call it
/// from (<c>Cors:AllowedOrigins</c>): browsers send it on every cross-origin call and every write, and
/// only writes publish events. Anything else carrying a user token is the API. This is for the audit
/// log to show, not a security check: a caller can send any Origin it likes.
/// </summary>
public sealed class HttpRequestOriginAccessor(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : IRequestOriginAccessor
{
    /// <summary>Long enough for any real browser's, and short enough that one row cannot carry an essay.</summary>
    internal const int MaxUserAgentLength = 256;

    private readonly string[] _consoleOrigins = configuration.GetCorsAllowedOrigins();

    public RequestOrigin? Current
    {
        get
        {
            if (httpContextAccessor.HttpContext is not { } context)
            {
                return null;
            }

            var origin = context.Request.Headers.Origin.ToString();
            var source = origin.Length > 0 && _consoleOrigins.IsCorsOriginAllowed(origin) ? EventSource.Console : EventSource.Api;
            var userAgent = context.Request.Headers.UserAgent.ToString();

            return new RequestOrigin(
                source,
                // Already the real caller's: UseForwardedHeaders runs first in the hosts behind the proxy.
                context.Connection.RemoteIpAddress?.ToString(),
                userAgent.Length == 0 ? null : userAgent.Length > MaxUserAgentLength ? userAgent[..MaxUserAgentLength] : userAgent);
        }
    }
}
