using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record LogicGateConfig
{
    /// <summary>
    /// The condition, as a structured expression tree - so an author can express
    /// <c>reading.temperature &gt; 30</c> rather than only a pre-computed boolean.
    ///
    /// Must evaluate to a boolean; anything else fails the node with LOGIC_CONDITION_NOT_BOOL.
    /// The converter still accepts the legacy bare-string form on read - see
    /// <see cref="LogicConditionJsonConverter"/>.
    /// </summary>
    [JsonPropertyName("condition")]
    [JsonConverter(typeof(LogicConditionJsonConverter))]
    public required WorkflowExpression Condition { get; init; }
}
