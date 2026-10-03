using Microsoft.Extensions.Configuration;

namespace Wbskt.Infrastructure.Configuration;

public static class CorsOriginsConfiguration
{
    /// <summary>
    /// The browser origins allowed to call a host: the <c>Cors:AllowedOrigins</c> array (the public
    /// console) plus <c>Cors:ExtraOrigins</c>, a comma-separated list. The extra list exists so an
    /// operator can let a console running elsewhere - typically <c>ng serve</c> on
    /// http://localhost:4200 - use a deployed backend by editing one .env line, without touching
    /// the console origin that email links are built from. It is empty unless someone opts in.
    /// An entry may end in <c>:*</c> (e.g. <c>http://localhost:*</c>) to allow any port on that
    /// scheme and host; see <see cref="IsCorsOriginAllowed"/>.
    /// </summary>
    public static string[] GetCorsAllowedOrigins(this IConfiguration configuration)
    {
        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var extra = (configuration["Cors:ExtraOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // CORS compares origins exactly, so a trailing slash copied from a browser address bar
        // would never match the Origin header the browser actually sends.
        return allowed
            .Concat(extra)
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Whether <paramref name="origin"/> matches one of <paramref name="allowedOrigins"/>: exactly,
    /// or, for an entry ending in <c>:*</c>, on scheme and host with any port. Only the port is
    /// wildcarded - never the host - so <c>http://localhost:*</c> cannot match another site.
    /// </summary>
    public static bool IsCorsOriginAllowed(this IReadOnlyCollection<string> allowedOrigins, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        foreach (var allowed in allowedOrigins)
        {
            if (allowed.EndsWith(":*", StringComparison.Ordinal))
            {
                if (Uri.TryCreate(allowed[..^2], UriKind.Absolute, out var allowedUri)
                    && string.Equals(allowedUri.Scheme, originUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(allowedUri.Host, originUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(allowed, origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
