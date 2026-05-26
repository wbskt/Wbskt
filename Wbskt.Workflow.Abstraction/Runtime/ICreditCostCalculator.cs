using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ICreditCostCalculator
{
    decimal Calculate(BaseNode node, NodeExecutionResult result);
}
