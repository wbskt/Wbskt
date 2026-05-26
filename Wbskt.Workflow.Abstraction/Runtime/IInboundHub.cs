namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IInboundHub
{
    Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct);
}
