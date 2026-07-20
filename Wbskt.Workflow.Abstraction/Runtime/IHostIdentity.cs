namespace Wbskt.Workflow.Abstraction.Runtime;

// Workflow-engine lease-holder identity (see SqlLeaseHolder, Leases table). Deliberately distinct
// from Wbskt.EventBus.RabbitMQ.BusInstanceId, which Socket.Host uses for MassTransit queue naming
// and socket presence: that id must stay stable across a restart of the same container so crash
// recovery can find its own stale rows again, whereas HostId here must be freshly generated on
// every process start so a restarted instance always fences out its own prior (possibly still
// straggling) lease rather than silently reclaiming it. Do not unify the two - each needs the
// other's lifetime to be wrong for its own use case to hold.
public interface IHostIdentity
{
    string HostId { get; }
}
