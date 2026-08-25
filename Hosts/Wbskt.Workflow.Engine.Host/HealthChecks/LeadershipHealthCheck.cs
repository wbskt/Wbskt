using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wbskt.Workflow.Engine;

namespace Wbskt.Workflow.Engine.Host.HealthChecks;

/// <summary>
/// Reports ready only on the instance that currently holds the engine lease and has finished
/// starting its bus — the same condition the <c>/healthz/ready</c> lambda used to test inline.
///
/// This is what keeps a standby out of Traefik's routing table. A standby is a healthy process
/// doing exactly what it should: waiting. It is simply not the instance that may process inbound
/// triggers, so it must not be sent any.
///
/// Leadership is asserted explicitly rather than inferred from the bus health check that MassTransit
/// registers alongside it. A standby's bus is stopped, so the two usually agree, but "the bus is up"
/// and "this instance holds the lease" are different claims and only the second one may gate traffic.
/// </summary>
internal sealed class LeadershipHealthCheck : IHealthCheck
{
    private readonly LeadershipState _leadershipState;

    public LeadershipHealthCheck(LeadershipState leadershipState)
    {
        _leadershipState = leadershipState;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_leadershipState.IsLeader)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("This instance is a standby, not the engine leader."));
        }

        if (!_leadershipState.BusStarted)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("This instance is the leader but its bus has not finished starting."));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
