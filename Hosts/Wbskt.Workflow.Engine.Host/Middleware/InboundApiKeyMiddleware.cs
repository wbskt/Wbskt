using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Engine.Host.Middleware;

// The engine stays backend-network-only regardless (never published by Traefik), but /api/inbound/*
// currently has no authentication of its own - this is the last line of defense for anything on
// the backend network. Production fails closed if Engine:InboundApiKey is unset; Development
// treats an unset key as "checks disabled" for local convenience.
public sealed class InboundApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Wbskt-Api-Key";

    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedApiKey;
    private readonly bool _isProduction;

    public InboundApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, IHostEnvironment environment)
    {
        _next = next;
        _isProduction = environment.IsProduction();

        string? configuredKey = configuration["Engine:InboundApiKey"];
        _expectedApiKey = string.IsNullOrEmpty(configuredKey) ? null : Encoding.UTF8.GetBytes(configuredKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/inbound"))
        {
            await _next(context);
            return;
        }

        if (_expectedApiKey == null)
        {
            if (_isProduction)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Engine:InboundApiKey is not configured.");
                return;
            }

            await _next(context);
            return;
        }

        byte[] providedKey = Encoding.UTF8.GetBytes(context.Request.Headers[ApiKeyHeaderName].ToString());
        if (!CryptographicOperations.FixedTimeEquals(providedKey, _expectedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }
}
