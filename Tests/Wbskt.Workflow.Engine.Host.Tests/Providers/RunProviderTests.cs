using FluentAssertions;
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

    [Fact]
    public void ListByWorkflow_Map_projects_multiple_rows()
    {
        var rows = new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 10,
                ["RefId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ["WorkflowDefinitionId"] = 5,
                ["WorkflowRefId"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ["WorkflowVersion"] = 2,
                ["TriggerNodeId"] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ["CorrelationKey"] = "key-a",
                ["Status"] = "Running",
                ["StartedAt"] = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = DBNull.Value,
                ["CancellationRequestedAt"] = DBNull.Value,
                ["CancellationReason"] = DBNull.Value,
                ["CreditBudget"] = 50m,
                ["CreatedAt"] = new DateTime(2026, 6, 1, 9, 59, 0, DateTimeKind.Utc)
            },
            new Dictionary<string, object?>
            {
                ["Id"] = 9,
                ["RefId"] = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ["WorkflowDefinitionId"] = 5,
                ["WorkflowRefId"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ["WorkflowVersion"] = 2,
                ["TriggerNodeId"] = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ["CorrelationKey"] = DBNull.Value,
                ["Status"] = "Completed",
                ["StartedAt"] = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = new DateTime(2026, 6, 1, 8, 5, 0, DateTimeKind.Utc),
                ["CancellationRequestedAt"] = DBNull.Value,
                ["CancellationReason"] = DBNull.Value,
                ["CreditBudget"] = 25m,
                ["CreatedAt"] = new DateTime(2026, 6, 1, 7, 59, 0, DateTimeKind.Utc)
            }
        };

        var reader = FakeSqlDataReader.From(rows);
        var results = new List<RunRow>();
        while (reader.Read())
        {
            results.Add(RunProvider.Map(reader));
        }

        results.Should().HaveCount(2);
        results[0].Id.Should().Be(10);
        results[0].Status.Should().Be("Running");
        results[0].CorrelationKey.Should().Be("key-a");
        results[1].Id.Should().Be(9);
        results[1].Status.Should().Be("Completed");
        results[1].CorrelationKey.Should().BeNull();
        results[1].CompletedAt.Should().Be(new DateTime(2026, 6, 1, 8, 5, 0, DateTimeKind.Utc));
    }
}
