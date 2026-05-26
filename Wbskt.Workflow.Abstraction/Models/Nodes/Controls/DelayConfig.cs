using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record DelayConfig([property: JsonPropertyName("duration")] TimeSpan Duration);
