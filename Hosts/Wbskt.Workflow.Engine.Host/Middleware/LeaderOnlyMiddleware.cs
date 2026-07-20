using Wbskt.Workflow.Engine;

namespace Wbskt.Workflow.Engine.Host.Middleware;

// A standby never starts the bus or recovers state (see EngineLeadershipCoordinator), so it must
// not process inbound triggers either. Traefik's /healthz/ready check keeps standby out of
// rotation in steady state; this is the defense for the failover gap and for any caller on the
// backend network that bypasses the proxy.
public sealed class LeaderOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, LeadershipState leadershipState)
    {
        // Gate on the same signal as /healthz/ready: a "leader" whose bus never finished starting
        // can't actually publish outbound events for a trigger it accepts, so it must not accept one.
        bool canServeInbound = leadershipState.IsLeader && leadershipState.BusStarted;
        bool isInboundEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<InboundEndpointAttribute>() != null;
        if (isInboundEndpoint && !canServeInbound)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            await context.Response.WriteAsync("This instance is not the workflow-engine leader.");
            return;
        }

        await next(context);
    }
}
