using Microsoft.AspNetCore.Authorization;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Engine.Host.Middleware;

// The engine stays backend-network-only regardless (never published by Traefik), but it has no
// user authentication of its own - this is the last line of defense for anything on the backend
// network. It denies by default: every endpoint needs the key except those marked [AllowAnonymous]
// (the health probes), so a controller added without [InboundEndpoint] is not left open. Without
// Engine:InboundApiKey only Development lets a call through; anywhere else it is refused.
public sealed class InboundApiKeyMiddleware(RequestDelegate next)
{
    public const string ConfigKey = "Engine:InboundApiKey";

    private static readonly SharedKeyCheck Key = new(ConfigKey, EngineInboundApi.ApiKeyHeader, "ENGINE_API_KEY", MissingSharedKey.RefuseOutsideDevelopment);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() == null && Key.Check(context) is { } refusal)
        {
            await refusal.WriteAsync(context);
            return;
        }

        await next(context);
    }
}
