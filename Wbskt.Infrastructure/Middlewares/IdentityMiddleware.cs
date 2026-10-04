using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure.Middlewares;

public class IdentityMiddleware
{
    private readonly RequestDelegate _next;

    public IdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdentityService identityService)
    {
        var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(userIdStr) && int.TryParse(userIdStr, out var userId))
        {
            var userIdentity = new UserIdentity(userId)
            {
                UserRefId = Guid.TryParse(context.User.FindFirst(JwtServiceCollectionExtensions.JwtUserRefClaim)?.Value, out var userRefId) ? userRefId : null
            };
            using (identityService.BeginScope(userIdentity))
            {
                await _next(context);
            }
        }
        else
        {
            await _next(context);
        }
    }
}
