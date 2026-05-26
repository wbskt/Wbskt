using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class DefaultCreditCostCalculator : ICreditCostCalculator
{
    // TODO: Phase 10 will implement actual cost calculation
    public decimal Calculate(BaseNode node, NodeExecutionResult result)
    {
        return 1.0m;
    }
}
