using FluentAssertions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class HistoryEventProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["HistoryEventId"] = 123L,
                ["RunId"] = 10,
                ["BranchRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["EventKind"] = "NodeStarted",
                ["Severity"] = "Info",
                ["PayloadJson"] = "{\"data\":\"test\"}",
                ["Timestamp"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = HistoryEventProvider.Map(reader);

        row.HistoryEventId.Should().Be(123L);
        row.RunId.Should().Be(10);
        row.BranchRefId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.NodeId.Should().Be(Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.EventKind.Should().Be("NodeStarted");
        row.Severity.Should().Be("Info");
        row.PayloadJson.Should().Be("{\"data\":\"test\"}");
        row.Timestamp.Should().Be(new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["HistoryEventId"] = 123L,
                ["RunId"] = 10,
                ["BranchRefId"] = null,
                ["NodeId"] = null,
                ["EventKind"] = "WorkflowStarted",
                ["Severity"] = "Info",
                ["PayloadJson"] = null,
                ["Timestamp"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = HistoryEventProvider.Map(reader);

        row.BranchRefId.Should().BeNull();
        row.NodeId.Should().BeNull();
        row.PayloadJson.Should().BeNull();
    }
}
