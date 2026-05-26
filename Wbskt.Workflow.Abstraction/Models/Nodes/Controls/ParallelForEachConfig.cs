using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ParallelForEachConfig([property: JsonPropertyName("collection")] string Collection);
