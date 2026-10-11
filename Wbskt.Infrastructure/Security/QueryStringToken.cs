using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// Lets the bearer handler read the token from <c>?access_token=</c> on the paths given. Browsers
/// cannot set headers on a WebSocket, so the socket host's <c>/ws</c> and the management host's
/// SignalR hub are reached that way; everywhere else only the Authorization header counts.
/// </summary>
public static class QueryStringToken
{
    public const string Parameter = "access_token";

    public static JwtBearerEvents AcceptQueryToken(this JwtBearerEvents events, params string[] paths)
    {
        var accepted = paths.Select(p => new PathString(p)).ToArray();
        var previous = events.OnMessageReceived;
        events.OnMessageReceived = async context =>
        {
            await previous(context);
            if (context.Token is null
                && string.IsNullOrEmpty(context.Request.Headers.Authorization)
                && accepted.Any(p => context.HttpContext.Request.Path.StartsWithSegments(p)))
            {
                var token = context.Request.Query[Parameter].ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }
            }
        };

        return events;
    }
}
