using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

public sealed class BaseNodeJsonConverter : JsonConverter<BaseNode>
{
    public override BaseNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var kind = root.GetProperty("kind").GetString();
        var nodeType = ResolveNodeType(kind);
        return (BaseNode)root.Deserialize(nodeType, options)!;
    }

    public override void Write(Utf8JsonWriter writer, BaseNode value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        var json = JsonSerializer.Serialize(value, type, options);
        using var doc = JsonDocument.Parse(json);

        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.NameEquals("kind"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    // The model type comes from the kind's descriptor, so a kind added there needs no edit here.
    private static Type ResolveNodeType(string? kind) =>
        NodeKinds.TryGet(kind, out NodeKindDescriptor? descriptor)
            ? descriptor.ModelType
            : throw new JsonException($"Unsupported node kind '{kind}'.");
}
