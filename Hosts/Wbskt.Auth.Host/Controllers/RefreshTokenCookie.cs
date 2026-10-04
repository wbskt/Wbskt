using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Controllers;

/// <summary><c>Auth:RefreshCookie</c>.</summary>
public sealed class RefreshCookieOptions
{
    public string Name { get; set; } = "wbskt_refresh";

    /// <summary>
    /// Strict suits a console on a sibling subdomain of the auth host (same site). A console served
    /// from another site, such as <c>ng serve</c> on localhost against a deployed backend, needs None,
    /// or the browser never sends the cookie back.
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;
}

/// <summary>
/// Keeps a browser's refresh token in an HttpOnly cookie, so script on the page (and so an XSS bug)
/// can never read the long-lived half of the session. A client opts in per request with the
/// <see cref="TransportHeader"/>; without it the token travels in the JSON body as before, which is
/// what the SDK, devices and tests use.
/// </summary>
/// <remarks>
/// The cookie is only ever read from a request that also carries the header. A custom header cannot
/// be sent cross-origin without a CORS preflight, which only allowed origins pass, so another site
/// cannot make the browser spend the cookie (CSRF). The Origin check is the same rule again for a
/// browser that skips the preflight.
/// </remarks>
public sealed class RefreshTokenCookie
{
    public const string TransportHeader = "X-Refresh-Token-Transport";
    public const string CookieTransport = "cookie";

    /// <summary>Scoped to the endpoints that read it, so no other request ever carries it.</summary>
    public const string CookiePath = "/api/auth";

    private readonly RefreshCookieOptions _options;
    private readonly TimeSpan _lifetime;
    private readonly string[] _allowedOrigins;
    private readonly bool _anyOrigin;

    public RefreshTokenCookie(IOptions<RefreshCookieOptions> options, IOptions<AccessTokenOptions> tokens, IConfiguration configuration, IHostEnvironment environment)
    {
        _options = options.Value;
        _lifetime = tokens.Value.RefreshTokenLifetime;
        _allowedOrigins = configuration.GetCorsAllowedOrigins();
        // Mirrors the CORS policy: Development with no origins configured lets any origin in.
        _anyOrigin = _allowedOrigins.Length == 0 && environment.IsDevelopment();
    }

    /// <summary>Whether the caller asked for the refresh token as a cookie instead of in the body.</summary>
    public static bool IsRequested(HttpRequest request) =>
        string.Equals(request.Headers[TransportHeader], CookieTransport, StringComparison.OrdinalIgnoreCase);

    /// <summary>The refresh token from the cookie, or null when there is none or the request may not use it.</summary>
    public string? Read(HttpRequest request) =>
        IsRequested(request) && IsOriginAllowed(request) && request.Cookies.TryGetValue(_options.Name, out var token) && !string.IsNullOrEmpty(token)
            ? token
            : null;

    public void Write(HttpResponse response, string token) =>
        response.Cookies.Append(_options.Name, token, CookieOptions(DateTimeOffset.UtcNow.Add(_lifetime)));

    public void Clear(HttpResponse response) =>
        response.Cookies.Delete(_options.Name, CookieOptions(expires: null));

    private bool IsOriginAllowed(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        // No Origin means no browser cross-origin request; a browser sends it on every CORS request.
        return origin.Length == 0 || _anyOrigin || _allowedOrigins.IsCorsOriginAllowed(origin);
    }

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = _options.SameSite,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true
    };
}
