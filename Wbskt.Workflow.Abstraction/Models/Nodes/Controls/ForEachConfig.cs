using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ForEachConfig([property: JsonPropertyName("collection")] string Collection);
