using Webskt.Common.Security;

namespace Webskt.Socket.Host.Middleware;

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
            var token = context.Request.Query["access_token"].ToString();

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
