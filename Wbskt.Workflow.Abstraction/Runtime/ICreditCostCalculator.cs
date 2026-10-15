using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ICreditCostCalculator
{
    /// <summary>
    /// The price of one attempt at <paramref name="node"/>. It is levied before the node runs, so that
    /// a run out of credits is stopped rather than billed after the fact; the price therefore cannot
    /// depend on the node's outcome.
    /// </summary>
    decimal Calculate(BaseNode node);
}
