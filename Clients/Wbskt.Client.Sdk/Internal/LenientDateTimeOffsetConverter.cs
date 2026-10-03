using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Client.Sdk.Internal;

/// <summary>
/// Reads an optional timestamp without failing the whole message: anything that is not an
/// ISO 8601 string becomes null, so a device with a bad clock format still gets its data through.
/// </summary>
internal sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            return value;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
