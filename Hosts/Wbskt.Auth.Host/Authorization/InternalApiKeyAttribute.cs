using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Authorization;

/// <summary>
/// Requires the shared internal key (<see cref="InternalRoutes.ApiKeyHeader"/>, configured as
/// <see cref="ConfigKey"/>) on an internal route that a background job calls, where there is no user
/// token to forward. The action also carries <c>[AllowAnonymous]</c>, because no bearer token comes
/// with the call; this filter is what stands in for it.
/// </summary>
/// <remarks>
/// Fails closed as the engine's <c>InboundApiKeyMiddleware</c> does: without a configured key,
/// Production refuses every call (503) and any other environment lets it through, for local runs.
/// The comparison is fixed-time.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string ConfigKey = "Internal:ApiKey";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        string? configured = services.GetRequiredService<IConfiguration>()[ConfigKey];

        if (string.IsNullOrEmpty(configured))
        {
            if (services.GetRequiredService<IHostEnvironment>().IsProduction())
            {
                context.Result = Problem(StatusCodes.Status503ServiceUnavailable, "INTERNAL_API_KEY_NOT_CONFIGURED", $"{ConfigKey} is not configured.");
            }

            return;
        }

        byte[] provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[InternalRoutes.ApiKeyHeader].ToString());
        if (!CryptographicOperations.FixedTimeEquals(provided, Encoding.UTF8.GetBytes(configured)))
        {
            context.Result = Problem(StatusCodes.Status401Unauthorized, "INTERNAL_API_KEY_INVALID", $"The {InternalRoutes.ApiKeyHeader} header is missing or wrong.");
        }
    }

    private static ObjectResult Problem(int status, string code, string detail) =>
        new(Problems.Create(status, code, detail))
        {
            StatusCode = status,
            ContentTypes = { Problems.ContentType }
        };
}
