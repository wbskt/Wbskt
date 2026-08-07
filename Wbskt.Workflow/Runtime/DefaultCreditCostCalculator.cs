using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Prices a node execution from <see cref="CreditCostOptions"/>.
/// </summary>
/// <remarks>
/// The charge happens once per <em>attempt</em>, inside the retry loop, so a node retried three times
/// costs three executions - which is what it actually consumed downstream.
/// </remarks>
internal sealed class DefaultCreditCostCalculator : ICreditCostCalculator
{
    private readonly CreditCostOptions _options;

    /// <param name="options">Optional so hand-built test callers get the default price list.</param>
    public DefaultCreditCostCalculator(IOptions<CreditCostOptions>? options = null)
    {
        _options = options?.Value ?? new CreditCostOptions();
    }

    /// <param name="result">
    /// Unused, and callers pass null: the charge is levied *before* the node runs, so that a run out of
    /// credits is stopped rather than billed after the fact. Kept on the interface for a future
    /// outcome-sensitive model.
    /// </param>
    public decimal Calculate(BaseNode node, NodeExecutionResult result)
    {
        _ = result;
        return _options.CostFor(node.Kind);
    }
}
