using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record FailRunConfig([property: JsonPropertyName("reason")] string? Reason = null);
