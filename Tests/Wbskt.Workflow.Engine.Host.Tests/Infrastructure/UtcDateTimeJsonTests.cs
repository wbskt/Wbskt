using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Json;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// Times read back from SQL have no kind, and a browser reads an offset-less timestamp as local
/// time. Every host's API writes them as UTC with a Z, and reads times as UTC too.
/// </summary>
public sealed class UtcDateTimeJsonTests
{
    private sealed record Row(DateTime CreatedAt, DateTime? ConnectedAt);

    private static readonly DateTime FromSql = new(2026, 10, 4, 12, 30, 15, 250, DateTimeKind.Unspecified);

    [Fact]
    public void A_time_read_from_the_database_is_written_as_utc()
    {
        var json = JsonSerializer.Serialize(new Row(FromSql, FromSql), WbsktJsonExtensions.Apply(new JsonSerializerOptions()));

        Assert.Equal("""{"CreatedAt":"2026-10-04T12:30:15.25Z","ConnectedAt":"2026-10-04T12:30:15.25Z"}""", json);
    }

    [Fact]
    public void A_missing_time_stays_null()
    {
        var json = JsonSerializer.Serialize(new Row(FromSql, null), WbsktJsonExtensions.Apply(new JsonSerializerOptions()));

        Assert.EndsWith("\"ConnectedAt\":null}", json);
    }

    [Fact]
    public void A_local_time_is_converted_rather_than_relabelled()
    {
        var local = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Local);

        var json = JsonSerializer.Serialize(local, WbsktJsonExtensions.Apply(new JsonSerializerOptions()));

        Assert.Equal($"\"{local.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}Z\"", json);
    }

    [Theory]
    [InlineData("\"2026-10-04T12:30:15Z\"")]
    [InlineData("\"2026-10-04T18:00:15+05:30\"")]
    [InlineData("\"2026-10-04T12:30:15\"")]
    public void A_time_in_a_request_is_read_as_utc(string json)
    {
        var value = JsonSerializer.Deserialize<DateTime>(json, WbsktJsonExtensions.Apply(new JsonSerializerOptions()));

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 10, 4, 12, 30, 15, DateTimeKind.Utc), value);
    }

    [Fact]
    public void Controllers_and_minimal_endpoints_both_get_it()
    {
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddWbsktJson();
        using var provider = services.BuildServiceProvider();

        var mvc = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var http = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        Assert.EndsWith("Z\"", JsonSerializer.Serialize(FromSql, mvc));
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(FromSql, http));
    }

    [Fact]
    public void Applying_twice_adds_the_converter_once()
    {
        var options = WbsktJsonExtensions.Apply(WbsktJsonExtensions.Apply(new JsonSerializerOptions()));

        Assert.Single(options.Converters, c => c is UtcDateTimeJsonConverter);
    }
}
