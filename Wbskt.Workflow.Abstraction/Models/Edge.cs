using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models;

[JsonConverter(typeof(EdgeJsonConverter))]
public sealed record Edge((Guid NodeId, string PortId) From, (Guid NodeId, string PortId) To);

public sealed class EdgeJsonConverter : JsonConverter<Edge>
{
    public override Edge Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var from = root.GetProperty("from");
        var to = root.GetProperty("to");
        return new Edge(
            (from[0].GetGuid(), from[1].GetString()!),
            (to[0].GetGuid(), to[1].GetString()!));
    }

    public override void Write(Utf8JsonWriter writer, Edge value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("from");
        writer.WriteStringValue(value.From.NodeId.ToString());
        writer.WriteStringValue(value.From.PortId);
        writer.WriteEndArray();
        writer.WriteStartArray("to");
        writer.WriteStringValue(value.To.NodeId.ToString());
        writer.WriteStringValue(value.To.PortId);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
