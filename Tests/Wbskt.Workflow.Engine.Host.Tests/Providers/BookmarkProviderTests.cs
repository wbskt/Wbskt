using FluentAssertions;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class BookmarkProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42,
                ["RefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["RunId"] = 10,
                ["BranchRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WakeConditionKind"] = "Signal",
                ["MatchKey"] = "test-key",
                ["WakeConditionJson"] = "{\"signal\":\"test\"}",
                ["ExpiresAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["TtlPort"] = "timeout",
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = BookmarkProvider.Map(reader);

        row.Id.Should().Be(42);
        row.RefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.RunId.Should().Be(10);
        row.BranchRefId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.NodeId.Should().Be(Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.WakeConditionKind.Should().Be("Signal");
        row.MatchKey.Should().Be("test-key");
        row.WakeConditionJson.Should().Be("{\"signal\":\"test\"}");
        row.ExpiresAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        row.TtlPort.Should().Be("timeout");
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42,
                ["RefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["RunId"] = 10,
                ["BranchRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WakeConditionKind"] = "Signal",
                ["MatchKey"] = "test-key",
                ["WakeConditionJson"] = "{\"signal\":\"test\"}",
                ["ExpiresAt"] = null,
                ["TtlPort"] = null,
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = BookmarkProvider.Map(reader);

        row.ExpiresAt.Should().BeNull();
        row.TtlPort.Should().BeNull();
    }
}
