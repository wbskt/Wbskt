using Wbskt.Common.Security;

namespace Wbskt.Socket.Host.Middleware;

public sealed class WebSocketAuthMiddleware
{
    private readonly RequestDelegate _next;

    public WebSocketAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IJwtService jwtService)
    {
        if (context.Request.Path == "/ws")
        {
            var token = string.Empty;

            // 1. Check Authorization header (Used by C# SDK and other clients that can set headers)
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }

            // 2. Check query string as fallback (Required for browser WebSockets like Simulator.Web)
            if (string.IsNullOrWhiteSpace(token))
            {
                token = context.Request.Query["access_token"].ToString();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            try
            {
                var principal = await jwtService.ValidateToken(token);
                
                // Ensure it's a client token
                var typeClaim = principal.FindFirst("type")?.Value;
                if (typeClaim != "client")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                context.User = principal;
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }
}
