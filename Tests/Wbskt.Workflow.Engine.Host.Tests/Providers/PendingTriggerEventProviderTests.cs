using FluentAssertions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class PendingTriggerEventProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42,
                ["WorkflowRefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["TriggerNodeId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["CorrelationKey"] = "user-123",
                ["InboundEventJson"] = "{\"event\":\"data\"}",
                ["EnqueuedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
                ["CreatedAt"] = new DateTime(2026, 5, 25, 11, 59, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = PendingTriggerEventProvider.Map(reader);

        row.Id.Should().Be(42);
        row.WorkflowRefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.TriggerNodeId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.CorrelationKey.Should().Be("user-123");
        row.InboundEventJson.Should().Be("{\"event\":\"data\"}");
        row.EnqueuedAt.Should().Be(new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc));
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 25, 11, 59, 0, DateTimeKind.Utc));
    }
}
