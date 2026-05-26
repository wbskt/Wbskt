using FluentAssertions;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class BranchProviderTests
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
                ["RunId"] = 42,
                ["ParentBranchId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["ForkCohortId"] = DBNull.Value,
                ["NodeId"] = Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["Status"] = "Active",
                ["PendingTakePort"] = "success",
                ["LocalJson"] = "{\"x\":1}",
                ["LastOutputJson"] = "{\"y\":2}",
                ["CompensationStackJson"] = DBNull.Value,
                ["CreatedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["UpdatedAt"] = new DateTime(2026, 5, 26, 12, 1, 0, DateTimeKind.Utc),
                ["RowVersion"] = new byte[] { 1, 2, 3, 4 }
            }
        });

        reader.Read();
        var row = BranchProvider.Map(reader);

        row.Id.Should().Be(1);
        row.RefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.RunId.Should().Be(42);
        row.ParentBranchId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.ForkCohortId.Should().BeNull();
        row.NodeId.Should().Be(Guid.Parse("b47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.Status.Should().Be("Active");
        row.PendingTakePort.Should().Be("success");
        row.LocalJson.Should().Be("{\"x\":1}");
        row.LastOutputJson.Should().Be("{\"y\":2}");
        row.CompensationStackJson.Should().BeNull();
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        row.UpdatedAt.Should().Be(new DateTime(2026, 5, 26, 12, 1, 0, DateTimeKind.Utc));
        row.RowVersion.Should().Equal(new byte[] { 1, 2, 3, 4 });
    }
}
