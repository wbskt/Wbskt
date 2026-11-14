using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Requires the admin host's key (<see cref="InternalRoutes.AdminApiKeyHeader"/>, configured as
/// <see cref="ConfigKey"/>) on an <see cref="InternalRoutes.AdminPrefix"/> route. The action also carries
/// <c>[AllowAnonymous]</c>: no user token comes with the call, and the staff member is named in its body.
/// </summary>
/// <remarks>
/// Without a configured key the route answers 404 in every environment, as if it did not exist, so a
/// host deployed without the key behaves exactly as before and the admin host shows the action as
/// unavailable. The comparison is fixed-time.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string ConfigKey = "Internal:AdminApiKey";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        string? configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[ConfigKey];
        if (string.IsNullOrEmpty(configured))
        {
            context.Result = new NotFoundResult();
            return;
        }

        byte[] provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[InternalRoutes.AdminApiKeyHeader].ToString());
        if (!CryptographicOperations.FixedTimeEquals(provided, Encoding.UTF8.GetBytes(configured)))
        {
            var status = StatusCodes.Status401Unauthorized;
            context.Result = new ObjectResult(Problems.Create(status, "ADMIN_API_KEY_INVALID", $"The {InternalRoutes.AdminApiKeyHeader} header is missing or wrong."))
            {
                StatusCode = status,
                ContentTypes = { Problems.ContentType }
            };
        }
    }
}
