using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>What a shared-key route does when its key is not configured.</summary>
public enum MissingSharedKey
{
    /// <summary>Lets the call through in Development, for local runs, and answers 503 anywhere else.</summary>
    RefuseOutsideDevelopment,

    /// <summary>Answers 404 in every environment, as if the route did not exist.</summary>
    NotFound
}

/// <summary>Why a shared-key check refused a call; a <see langword="null"/> code is a bare 404.</summary>
public sealed record SharedKeyRefusal(int Status, string? Code, string? Detail)
{
    public static readonly SharedKeyRefusal NotFound = new(StatusCodes.Status404NotFound, null, null);

    public IActionResult ToResult() => Code is null
        ? new NotFoundResult()
        : new ObjectResult(Problems.Create(Status, Code, Detail))
        {
            StatusCode = Status,
            ContentTypes = { Problems.ContentType }
        };

    public Task WriteAsync(HttpContext context) => Code is null
        ? Problems.WriteAsync(context, Status, "NOT_FOUND", null)
        : Problems.WriteAsync(context, Status, Code, Detail);
}

/// <summary>
/// The one check of a key shared between hosts: the configured key against a request header, compared
/// in fixed time. The internal key, the admin key and the engine's inbound key all go through it, so
/// they differ only in their names and in what a missing key means.
/// </summary>
/// <param name="configKey">The configuration key holding the shared key.</param>
/// <param name="header">The request header carrying it.</param>
/// <param name="codePrefix">The start of the refusal codes, as in <c>INTERNAL_API_KEY</c>_INVALID.</param>
/// <param name="whenMissing">What a missing key means.</param>
public sealed class SharedKeyCheck(string configKey, string header, string codePrefix, MissingSharedKey whenMissing)
{
    /// <summary>The refusal, or <see langword="null"/> when the call may go on.</summary>
    public SharedKeyRefusal? Check(HttpContext context)
    {
        var services = context.RequestServices;
        string? configured = services.GetRequiredService<IConfiguration>()[configKey];
        if (string.IsNullOrEmpty(configured))
        {
            if (whenMissing == MissingSharedKey.NotFound)
            {
                return SharedKeyRefusal.NotFound;
            }

            return services.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? null
                : new SharedKeyRefusal(StatusCodes.Status503ServiceUnavailable, $"{codePrefix}_NOT_CONFIGURED", $"{configKey} is not configured.");
        }

        byte[] provided = Encoding.UTF8.GetBytes(context.Request.Headers[header].ToString());
        return CryptographicOperations.FixedTimeEquals(provided, Encoding.UTF8.GetBytes(configured))
            ? null
            : new SharedKeyRefusal(StatusCodes.Status401Unauthorized, $"{codePrefix}_INVALID", $"The {header} header is missing or wrong.");
    }
}
