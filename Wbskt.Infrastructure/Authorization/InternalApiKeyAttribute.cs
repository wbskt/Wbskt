using Microsoft.AspNetCore.Mvc.Filters;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Requires the shared internal key (<see cref="InternalRoutes.ApiKeyHeader"/>, configured as
/// <see cref="ConfigKey"/>) on an internal route that a background job calls, where there is no user
/// token to forward, or that an operator calls from inside the backend network. The action also carries
/// <c>[AllowAnonymous]</c>, because no bearer token comes with the call; this filter is what stands in for it.
/// </summary>
/// <remarks>
/// Without a configured key, only Development lets the call through (for local runs); anywhere else it
/// is refused with 503. See <see cref="SharedKeyCheck"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string ConfigKey = "Internal:ApiKey";

    private static readonly SharedKeyCheck Key = new(ConfigKey, InternalRoutes.ApiKeyHeader, "INTERNAL_API_KEY", MissingSharedKey.RefuseOutsideDevelopment);

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (Key.Check(context.HttpContext) is { } refusal)
        {
            context.Result = refusal.ToResult();
        }
    }
}
