namespace Wbskt.Workflow.Abstraction.Models.Nodes;

/// <summary>What part a node kind plays in a workflow, as its kind prefix spells it.</summary>
public enum NodeCategory
{
    /// <summary><c>trigger:*</c> - starts a run and hands the inbound payload on.</summary>
    Trigger,

    /// <summary><c>control:*</c> - steers the run without leaving the platform.</summary>
    Control,

    /// <summary><c>action:*</c> - does something outside the run: a message, a call, a notification.</summary>
    Action
}

/// <summary>
/// Everything the engine knows about a node kind without running it: the model the converter builds,
/// the config it carries, the ports the validator expects, and what it costs.
/// </summary>
/// <remarks>
/// One entry in <see cref="NodeKinds.All"/> per kind, so adding a kind is one descriptor plus its
/// executor. Before, the same knowledge sat in five hand-kept tables, and the failure modes of a
/// missed one were quiet: a kind absent from the port table published with no port check, and one
/// absent from the price list was billed at the default.
///
/// <para>Whether a node may be replayed is deliberately not here: it depends on the node's config
/// (a local variable Set is safe, a shared Increment is not), so the executor answers it per node.</para>
/// </remarks>
/// <param name="Kind">The <see cref="NodeKind"/> discriminator, as written in the definition JSON.</param>
/// <param name="Category">Trigger, control or action; must agree with the kind's prefix.</param>
/// <param name="ModelType">The <see cref="BaseNode"/> subtype the JSON converter builds for this kind.</param>
/// <param name="ConfigType">The type of the model's <c>Config</c>, or null for a kind with no config.</param>
/// <param name="DefaultCreditCost">
/// The built-in price of one execution. Triggers carry 0 and are never charged, whatever the
/// configuration says; see <c>DefaultCreditCostCalculator</c>.
/// </param>
/// <param name="ExpectedPorts">
/// The ports a node of this kind must declare, and the only ones it may declare (an action may also
/// add an optional "error" output). A function of the node because some kinds' ports follow their
/// config: a Fork's branch names, an AwaitSignal's timeout, a send-command step that awaits its ack.
/// </param>
public sealed record NodeKindDescriptor(
    string Kind,
    NodeCategory Category,
    Type ModelType,
    Type? ConfigType,
    decimal DefaultCreditCost,
    Func<BaseNode, IReadOnlyCollection<PortDefinition>> ExpectedPorts);
