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
}
