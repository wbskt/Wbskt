using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Engine;

namespace Wbskt.Workflow.Engine.Host.Middleware;

// A standby never starts the bus or recovers state (see EngineLeadershipCoordinator), so it must
// not process inbound triggers either. Traefik's /healthz/ready check keeps standby out of
// rotation in steady state; this is the defense for the failover gap and for any caller on the
// backend network that bypasses the proxy.
public sealed class LeaderOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILeadershipGate leadershipGate)
    {
        // The same gate the periodic work and /healthz/ready use: a "leader" whose bus never finished
        // starting can't publish outbound events for a trigger it accepts, and one whose lease is inside
        // its safety margin may be replaced before the trigger's run is under way.
        bool canServeInbound = leadershipGate.IsActive;
        bool isInboundEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<InboundEndpointAttribute>() != null;
        if (isInboundEndpoint && !canServeInbound)
        {
            context.Response.Headers.RetryAfter = ApiErrorResults.RetryAfterSeconds;
            await Problems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "ENGINE_NOT_LEADER", "This instance is not the workflow-engine leader.");
            return;
        }

        await next(context);
    }
}
