using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record LogicGateConfig([property: JsonPropertyName("condition")] string Condition);
