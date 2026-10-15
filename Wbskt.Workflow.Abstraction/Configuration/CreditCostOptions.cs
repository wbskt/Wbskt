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
