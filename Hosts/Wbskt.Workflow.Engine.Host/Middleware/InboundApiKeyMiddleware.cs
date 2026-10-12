using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Engine.Host.Middleware;

// The engine stays backend-network-only regardless (never published by Traefik), but it has no
// user authentication of its own - this is the last line of defense for anything on the backend
// network. It denies by default: every endpoint needs the key except those marked [AllowAnonymous]
// (the health probes), so a controller added without [InboundEndpoint] is not left open. Production
// fails closed if Engine:InboundApiKey is unset; Development treats an unset key as "checks
// disabled" for local convenience.
public sealed class InboundApiKeyMiddleware
{
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
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        if (_expectedApiKey == null)
        {
            if (_isProduction)
            {
                await Problems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "ENGINE_API_KEY_NOT_CONFIGURED", "Engine:InboundApiKey is not configured.");
                return;
            }

            await _next(context);
            return;
        }

        byte[] providedKey = Encoding.UTF8.GetBytes(context.Request.Headers[EngineInboundApi.ApiKeyHeader].ToString());
        if (!CryptographicOperations.FixedTimeEquals(providedKey, _expectedApiKey))
        {
            await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, "ENGINE_API_KEY_INVALID", $"The {EngineInboundApi.ApiKeyHeader} header is missing or wrong.");
            return;
        }

        await _next(context);
    }
}
