using FluentAssertions;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

public class FakeSqlDataReaderTests
{
    [Fact]
    public void FakeSqlDataReader_returns_rows_in_order()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "a" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "b" },
        });

        reader.Read().Should().BeTrue();
        reader.GetInt32(reader.GetOrdinal("Id")).Should().Be(1);
        reader.GetString(reader.GetOrdinal("Name")).Should().Be("a");
        reader.Read().Should().BeTrue();
        reader.GetInt32(reader.GetOrdinal("Id")).Should().Be(2);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void FakeSqlDataReader_handles_GetGuid()
    {
        var guid = Guid.NewGuid();
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?> { ["RefId"] = guid },
        });

        reader.Read().Should().BeTrue();
        reader.GetGuid(reader.GetOrdinal("RefId")).Should().Be(guid);
    }

    [Fact]
    public void FakeSqlDataReader_handles_GetDateTime()
    {
        var now = new DateTime(2026, 5, 26, 10, 30, 0, DateTimeKind.Utc);
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?> { ["CreatedAt"] = now },
        });

        reader.Read().Should().BeTrue();
        reader.GetDateTime(reader.GetOrdinal("CreatedAt")).Should().Be(now);
    }

    [Fact]
    public void FakeSqlDataReader_handles_IsDBNull()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?> { ["Name"] = "test", ["Description"] = null },
        });

        reader.Read().Should().BeTrue();
        reader.IsDBNull(reader.GetOrdinal("Name")).Should().BeFalse();
        reader.IsDBNull(reader.GetOrdinal("Description")).Should().BeTrue();
    }

    [Fact]
    public void FakeSqlDataReader_handles_byte_array()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?> { ["Data"] = bytes },
        });

        reader.Read().Should().BeTrue();
        var result = new byte[4];
        reader.GetBytes(reader.GetOrdinal("Data"), 0, result, 0, 4);
        result.Should().BeEquivalentTo(bytes);
    }
}