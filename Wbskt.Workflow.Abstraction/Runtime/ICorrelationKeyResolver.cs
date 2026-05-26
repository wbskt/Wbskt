namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ICorrelationKeyResolver
{
    string Resolve(InboundEvent evt);
}
