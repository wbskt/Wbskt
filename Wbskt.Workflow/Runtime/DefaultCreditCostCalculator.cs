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
    // A run's root branch starts at its trigger node, which only hands the inbound payload on. With no
    // entry in the price list every trigger fell through to DefaultCost, so every run paid a full
    // credit before its first real node. Excluded here rather than priced at zero in the table, which
    // keeps the table's rule that every kind it prices costs something.
    private static readonly IReadOnlySet<string> TriggerKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        NodeKind.TriggerClient,
        NodeKind.TriggerClientPresence,
        NodeKind.TriggerSchedule,
        NodeKind.TriggerWebhook,
        NodeKind.TriggerManual
    };

    private readonly CreditCostOptions _options;

    /// <param name="options">Optional so hand-built test callers get the default price list.</param>
    public DefaultCreditCostCalculator(IOptions<CreditCostOptions>? options = null)
    {
        _options = options?.Value ?? new CreditCostOptions();
    }

    public decimal Calculate(BaseNode node)
    {
        return TriggerKinds.Contains(node.Kind) ? 0m : _options.CostFor(node.Kind);
    }
}
