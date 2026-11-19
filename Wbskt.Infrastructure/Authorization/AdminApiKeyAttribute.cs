using Microsoft.AspNetCore.Mvc.Filters;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Requires the admin host's key (<see cref="InternalRoutes.AdminApiKeyHeader"/>, configured as
/// <see cref="ConfigKey"/>) on an <see cref="InternalRoutes.AdminPrefix"/> route. The action also carries
/// <c>[AllowAnonymous]</c>: no user token comes with the call, and the staff member is named in its body.
/// </summary>
/// <remarks>
/// Without a configured key the route answers 404 in every environment, as if it did not exist, so a
/// host deployed without the key behaves exactly as before and the admin host shows the action as
/// unavailable. See <see cref="SharedKeyCheck"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string ConfigKey = "Internal:AdminApiKey";

    private static readonly SharedKeyCheck Key = new(ConfigKey, InternalRoutes.AdminApiKeyHeader, "ADMIN_API_KEY", MissingSharedKey.NotFound);

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (Key.Check(context.HttpContext) is { } refusal)
        {
            context.Result = refusal.ToResult();
        }
    }
}
