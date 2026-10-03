using System.Text.Json;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// One text form per JSON value, so values that mean the same thing compare equal as strings.
/// </summary>
/// <remarks>
/// SharedVariable_CompareAndSet compares <c>ValueJson = @Expected</c> as text, which is what makes it
/// atomic. Plain serialization keeps a number's original spelling and an object's property order, so
/// <c>1</c> and <c>1.0</c>, or <c>{"a":1,"b":2}</c> and <c>{"b":2,"a":1}</c>, would never match. Every
/// value written to or compared against a variable goes through here instead: properties sorted by
/// ordinal name, numbers written in their shortest form (trailing zeros and exponents gone).
/// </remarks>
internal static class CanonicalJson
{
    public static string Serialize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                // A repeated name keeps its last value, as every JSON reader here does.
                var properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    properties[property.Name] = property.Value;
                }

                foreach ((string name, JsonElement propertyValue) in properties)
                {
                    writer.WritePropertyName(name);
                    Write(writer, propertyValue);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Number:
                if (value.TryGetDecimal(out decimal number))
                {
                    // Dividing by 1 at the largest scale strips trailing zeros: 1.50m becomes 1.5m.
                    writer.WriteNumberValue(number / 1.0000000000000000000000000000m);
                }
                else
                {
                    // Beyond decimal's range; the shortest round-trip double is the stable form.
                    writer.WriteNumberValue(value.GetDouble());
                }

                break;

            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                value.WriteTo(writer);
                break;
        }
    }
}
