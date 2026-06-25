using FluentAssertions;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class TriggerRegistrationProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 1,
                ["WorkflowDefinitionId"] = 42,
                ["WorkflowRefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowVersion"] = 3,
                ["TriggerNodeId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["TriggerKind"] = "Event",
                ["TriggerKey"] = "client.registered",
                ["CorrelationExpression"] = "$.clientId",
                ["ConcurrencyPolicy"] = "Queue",
                ["FilterExpression"] = "$.status == 'active'",
                ["CreatedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = TriggerRegistrationProvider.Map(reader);

        row.Id.Should().Be(1);
        row.WorkflowDefinitionId.Should().Be(42);
        row.WorkflowRefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.WorkflowVersion.Should().Be(3);
        row.TriggerNodeId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.TriggerKind.Should().Be("Event");
        row.TriggerKey.Should().Be("client.registered");
        row.CorrelationExpression.Should().Be("$.clientId");
        row.ConcurrencyPolicy.Should().Be("Queue");
        row.FilterExpression.Should().Be("$.status == 'active'");
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 1,
                ["WorkflowDefinitionId"] = 42,
                ["WorkflowRefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowVersion"] = 3,
                ["TriggerNodeId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["TriggerKind"] = "Event",
                ["TriggerKey"] = "client.registered",
                ["CorrelationExpression"] = DBNull.Value,
                ["ConcurrencyPolicy"] = "Queue",
                ["FilterExpression"] = DBNull.Value,
                ["CreatedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = TriggerRegistrationProvider.Map(reader);

        row.CorrelationExpression.Should().BeNull();
        row.FilterExpression.Should().BeNull();
    }
}
