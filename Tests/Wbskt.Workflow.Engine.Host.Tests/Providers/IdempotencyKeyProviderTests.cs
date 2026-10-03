using FluentAssertions;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class IdempotencyKeyProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42L,
                ["KeyValue"] = "test-key-123",
                ["RunId"] = 10,
                ["BranchRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["Attempt"] = 1,
                ["Status"] = "Succeeded",
                ["ResultJson"] = "{\"result\":\"success\"}",
                ["ErrorJson"] = null,
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = new DateTime(2026, 5, 25, 12, 1, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = IdempotencyKeyProvider.Map(reader);

        row.Id.Should().Be(42);
        row.KeyValue.Should().Be("test-key-123");
        row.RunId.Should().Be(10);
        row.BranchRefId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.NodeId.Should().Be(Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.Attempt.Should().Be(1);
        row.Status.Should().Be("Succeeded");
        row.ResultJson.Should().Be("{\"result\":\"success\"}");
        row.ErrorJson.Should().BeNull();
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc));
        row.CompletedAt.Should().Be(new DateTime(2026, 5, 25, 12, 1, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42L,
                ["KeyValue"] = "test-key-123",
                ["RunId"] = 10,
                ["BranchRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["Attempt"] = 1,
                ["Status"] = "Pending",
                ["ResultJson"] = null,
                ["ErrorJson"] = null,
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
                ["CompletedAt"] = null
            }
        });

        reader.Read();
        var row = IdempotencyKeyProvider.Map(reader);

        row.ResultJson.Should().BeNull();
        row.ErrorJson.Should().BeNull();
        row.CompletedAt.Should().BeNull();
    }
}
