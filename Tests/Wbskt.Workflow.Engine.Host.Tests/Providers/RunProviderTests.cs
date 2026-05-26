using FluentAssertions;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class RunProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 1,
                ["RefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowDefinitionId"] = 42,
                ["WorkflowRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowVersion"] = 3,
                ["TriggerNodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["CorrelationKey"] = "client-123",
                ["Status"] = "Running",
                ["StartedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = new DateTime(2026, 5, 26, 12, 5, 0, DateTimeKind.Utc),
                ["CancellationRequestedAt"] = DBNull.Value,
                ["CancellationReason"] = DBNull.Value,
                ["CreditBudget"] = 100.5m,
                ["CreatedAt"] = new DateTime(2026, 5, 26, 11, 59, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = RunProvider.Map(reader);

        row.Id.Should().Be(1);
        row.RefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.WorkflowDefinitionId.Should().Be(42);
        row.WorkflowRefId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.WorkflowVersion.Should().Be(3);
        row.TriggerNodeId.Should().Be(Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.CorrelationKey.Should().Be("client-123");
        row.Status.Should().Be("Running");
        row.StartedAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        row.CompletedAt.Should().Be(new DateTime(2026, 5, 26, 12, 5, 0, DateTimeKind.Utc));
        row.CancellationRequestedAt.Should().BeNull();
        row.CancellationReason.Should().BeNull();
        row.CreditBudget.Should().Be(100.5m);
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 26, 11, 59, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 1,
                ["RefId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowDefinitionId"] = 42,
                ["WorkflowRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowVersion"] = 3,
                ["TriggerNodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["CorrelationKey"] = DBNull.Value,
                ["Status"] = "Running",
                ["StartedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = DBNull.Value,
                ["CancellationRequestedAt"] = DBNull.Value,
                ["CancellationReason"] = DBNull.Value,
                ["CreditBudget"] = 100.5m,
                ["CreatedAt"] = new DateTime(2026, 5, 26, 11, 59, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = RunProvider.Map(reader);

        row.CorrelationKey.Should().BeNull();
        row.CompletedAt.Should().BeNull();
        row.CancellationRequestedAt.Should().BeNull();
        row.CancellationReason.Should().BeNull();
    }
}
