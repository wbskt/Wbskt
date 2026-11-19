using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary><c>dbo.MessageTemplate_Create</c>: the workspace's limit, and one name per workspace.</summary>
[Collection("SqlEdge")]
public sealed class MessageTemplateIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_workspace_holds_up_to_its_limit_and_one_template_per_name()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        var provider = Provider();

        await provider.InsertAsync(workspaceId, null, "Lamp on", "lamp", "{}", maxPerWorkspace: 2);

        var taken = await Assert.ThrowsAsync<SqlException>(() => provider.InsertAsync(workspaceId, null, "lamp ON", "lamp", "{}", maxPerWorkspace: 2));
        Assert.Equal(2627, taken.Number);

        // Another workspace may use the same name.
        await provider.InsertAsync(workspaceId + 1, null, "Lamp on", "lamp", "{}", maxPerWorkspace: 2);

        await provider.InsertAsync(workspaceId, null, "Lamp off", "lamp", "{}", maxPerWorkspace: 2);
        var full = await Assert.ThrowsAsync<SqlException>(() => provider.InsertAsync(workspaceId, null, "Lamp dim", "lamp", "{}", maxPerWorkspace: 2));
        Assert.Equal(50045, full.Number);
    }

    private IMessageTemplateProvider Provider() =>
        new MessageTemplateProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());
}
