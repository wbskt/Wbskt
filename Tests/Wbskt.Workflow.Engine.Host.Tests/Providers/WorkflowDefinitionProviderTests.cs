using FluentAssertions;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class WorkflowDefinitionProviderTests
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
                ["Version"] = 3,
                ["WorkspaceId"] = 1,
                ["Name"] = "My Workflow",
                ["Description"] = "Test workflow",
                ["PublishedBy"] = 10,
                ["DefinitionJson"] = "{\"nodes\":[]}",
                ["IsEnabled"] = true,
                ["CreatedAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = WorkflowDefinitionProvider.Map(reader);

        row.Id.Should().Be(42);
        row.RefId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.Version.Should().Be(3);
        row.WorkspaceId.Should().Be(1);
        row.Name.Should().Be("My Workflow");
        row.Description.Should().Be("Test workflow");
        row.IsEnabled.Should().BeTrue();
        row.DefinitionJson.Should().Be("{\"nodes\":[]}");
        row.PublishedBy.Should().Be(10);
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
    }
}
