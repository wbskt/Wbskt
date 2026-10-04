using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.Json;

/// <summary>
/// Writes every <see cref="DateTime"/> as UTC with a <c>Z</c>, and reads every one as UTC.
/// </summary>
/// <remarks>
/// Every time Wbskt stores is UTC (<c>SYSUTCDATETIME()</c> in SQL, <c>DateTime.UtcNow</c> in code),
/// but a <c>DATETIME2</c> read back from SQL has <see cref="DateTimeKind.Unspecified"/>, and
/// System.Text.Json writes that without an offset. A browser parses an offset-less timestamp as
/// local time, so in India every time read from the database showed 5 h 30 min off, while times
/// built in code (the SignalR feed) were right. Unspecified is therefore taken to mean UTC. A
/// <see cref="DateTimeKind.Local"/> value is converted, and a timestamp read with an offset comes
/// back as UTC rather than as this server's local time.
/// </remarks>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        AsUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(AsUtc(value));

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public static class WbsktJsonExtensions
{
    /// <summary>
    /// The JSON settings every host's API shares: controllers and minimal endpoints alike write
    /// times as UTC (see <see cref="UtcDateTimeJsonConverter"/>).
    /// </summary>
    public static IServiceCollection AddWbsktJson(this IServiceCollection services)
    {
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => Apply(options.JsonSerializerOptions));
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => Apply(options.SerializerOptions));
        return services;
    }

    /// <summary>Applies the shared settings to options configured elsewhere, such as SignalR's.</summary>
    public static JsonSerializerOptions Apply(JsonSerializerOptions options)
    {
        if (!options.Converters.Any(c => c is UtcDateTimeJsonConverter))
        {
            options.Converters.Add(new UtcDateTimeJsonConverter());
        }

        return options;
    }
}
