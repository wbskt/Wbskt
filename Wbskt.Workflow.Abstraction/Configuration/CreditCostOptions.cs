using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Configuration;

/// <summary>
/// What each node kind costs to execute, in credits.
/// </summary>
/// <remarks>
/// Configuration-driven rather than compiled in, because pricing is an operator decision that changes
/// without a release. Bind from <c>WorkflowEngine:CreditCosts</c>; anything not overridden keeps the
/// built-in value below.
///
/// <para>The shape of the built-in table is deliberate: work that leaves the process costs
/// meaningfully more than work that does not. A webhook call occupies a connection, waits on somebody
/// else's server and can retry; a local variable set is a dictionary write. Charging both 1.0 - as the
/// stub did - made <c>CreditBudget</c> a node-execution ceiling wearing a billing label, so a workflow
/// of a thousand cheap control nodes and one of a thousand outbound calls were indistinguishable to
/// the operator paying for them.</para>
///
/// <para>Retries are charged per attempt, because the charge sits inside the retry loop: three
/// attempts at a webhook cost three webhooks, which is what they actually consumed.</para>
/// </remarks>
public sealed class CreditCostOptions
{
    /// <summary>Charged for any kind with no explicit entry - including kinds added after this table.</summary>
    public decimal DefaultCost { get; init; } = 1.0m;

    /// <summary>
    /// Charged once when a run is created, test runs and sub-workflow children included.
    /// </summary>
    /// <remarks>
    /// Every run writes a run row, its counters and its history whatever its nodes cost, so a run of a
    /// trigger and a few control nodes is not free to host. Charging the start steers a trigger that
    /// fires on every reading towards a filter or a hold trigger instead of one run per reading.
    /// </remarks>
    public decimal RunStartCost { get; init; } = 1.0m;

    /// <summary>Per-kind overrides, keyed by <see cref="NodeKind"/> string. Merged over <see cref="BuiltIn"/>.</summary>
    public IReadOnlyDictionary<string, decimal> PerKind { get; init; } = new Dictionary<string, decimal>(StringComparer.Ordinal);

    /// <summary>
    /// The default price list, read off each kind's <see cref="NodeKindDescriptor.DefaultCreditCost"/>.
    /// Roughly: outbound I/O 5, in-process messaging 1, bookkeeping 0.1.
    /// </summary>
    /// <remarks>
    /// Triggers are left out rather than listed at zero: they are never charged (the calculator
    /// excludes them), and leaving them out keeps this table's rule that every kind it prices costs
    /// something.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, decimal> BuiltIn = NodeKinds.All
        .Where(descriptor => descriptor.Category != NodeCategory.Trigger)
        .ToDictionary(descriptor => descriptor.Kind, descriptor => descriptor.DefaultCreditCost, StringComparer.Ordinal);

    /// <summary>
    /// Throws when any configured price is negative. A negative price would refund credits on every
    /// execution, so a typo in configuration fails the host at start instead of minting credits.
    /// </summary>
    public void EnsureValid()
    {
        List<string> negative = PerKind.Where(entry => entry.Value < 0).Select(entry => $"PerKind:{entry.Key}").ToList();
        if (DefaultCost < 0)
        {
            negative.Add(nameof(DefaultCost));
        }

        if (RunStartCost < 0)
        {
            negative.Add(nameof(RunStartCost));
        }

        if (negative.Count > 0)
        {
            throw new InvalidOperationException($"WorkflowEngine:CreditCosts has negative prices: {string.Join(", ", negative)}.");
        }
    }

    /// <summary>Resolves the cost of a kind: explicit override, then built-in, then the default.</summary>
    public decimal CostFor(string kind)
    {
        if (PerKind.TryGetValue(kind, out decimal configured))
        {
            return configured;
        }

        return BuiltIn.TryGetValue(kind, out decimal builtIn) ? builtIn : DefaultCost;
    }
}
