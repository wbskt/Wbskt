using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure.Middlewares;

/// <summary>
/// Makes the signed-in user this request's <see cref="UserIdentity"/>. A token names the user by
/// RefId (<see cref="WbsktClaims"/>); the internal id this host's records use comes from
/// <see cref="IUserIdLookup"/>, or straight from a token issued before #306.
/// </summary>
public class IdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdentityMiddleware> _logger;

    public IdentityMiddleware(RequestDelegate next, ILogger<IdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdentityService identityService)
    {
        if (!WbsktClaims.TryReadUser(context.User, out Guid userRef, out int? userId))
        {
            await _next(context);
            return;
        }

        if (userId is null && context.RequestServices.GetService<IUserIdLookup>() is { } lookup)
        {
            try
            {
                userId = await lookup.FindUserIdAsync(userRef, context.RequestAborted);
            }
            catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
            {
                // Not a 401: the token is fine, and a console signs its user out on 401.
                _logger.LogWarning("Could not resolve user {UserRef}: {Message}", userRef, ex.Message);
                context.Response.Headers.RetryAfter = ApiErrorResults.RetryAfterSeconds;
                await Problems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "AUTH_UNAVAILABLE", "Who you are could not be confirmed right now. Try again shortly.");
                return;
            }
        }

        if (userId is not > 0)
        {
            await _next(context);
            return;
        }

        using (identityService.BeginScope(new UserIdentity(userId.Value) { UserRefId = userRef }))
        {
            await _next(context);
        }
    }
}
