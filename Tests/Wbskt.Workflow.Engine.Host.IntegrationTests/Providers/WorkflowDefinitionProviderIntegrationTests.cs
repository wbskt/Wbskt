using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>Task 13.2 — WorkflowDefinitionProvider: insert, retrieve, deprecate round-trips.</summary>
[Collection("SqlEdge")]
public sealed class WorkflowDefinitionProviderIntegrationTests(SqlEdgeFixture fixture)
{
    private static WorkflowDefinitionRow BuildRow(Guid refId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = 1,
        Name = $"IT-Workflow-{refId:N}",
        Description = "Integration test workflow",
       
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };

    [SkippableFact]
    public async Task Insert_then_GetByRefIdVersion_round_trips()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        inserted.RefId.Should().Be(refId);
        inserted.Version.Should().BeGreaterThan(0);
        inserted.Name.Should().StartWith("IT-Workflow-");

        WorkflowDefinitionRow retrieved = (await provider.FindRowByRefIdVersionAsync(refId, inserted.Version, CancellationToken.None))!;

        retrieved.Id.Should().Be(inserted.Id);
        retrieved.RefId.Should().Be(refId);

        // Not compared byte-for-byte with the input: WorkflowDefinition_Publish is the single
        // authority on the version number and stamps it into the JSON under the same HOLDLOCK that
        // computed it. So the stored document is the submitted one plus that property, and asserting
        // equality with the input would be asserting that the stamping does not happen.
        using JsonDocument stored = JsonDocument.Parse(retrieved.DefinitionJson);
        JsonElement root = stored.RootElement;

        root.GetProperty("nodes").GetArrayLength().Should().Be(0);
        root.GetProperty("edges").GetArrayLength().Should().Be(0);
        root.GetProperty("version").GetInt32().Should().Be(inserted.Version);
        root.TryGetProperty("isEnabled", out _).Should().BeFalse("whether a workflow runs is its state, not its definition's");
    }

    [SkippableFact]
    public async Task GetByIds_reads_every_row_asked_for_in_one_call_and_FindWorkspaceId_reads_the_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow a = await provider.InsertAsync(BuildRow(Guid.NewGuid()), CancellationToken.None);
        WorkflowDefinitionRow b = await provider.InsertAsync(BuildRow(Guid.NewGuid()), CancellationToken.None);

        IReadOnlyCollection<WorkflowDefinitionRow> rows = await provider.GetByIdsAsync([a.Id, b.Id, int.MaxValue], CancellationToken.None);

        rows.Select(r => r.Id).Should().BeEquivalentTo([a.Id, b.Id]);
        rows.Single(r => r.Id == b.Id).Name.Should().Be(b.Name);
        (await provider.FindWorkspaceIdAsync(a.Id, CancellationToken.None)).Should().Be(1);
        (await provider.FindWorkspaceIdAsync(int.MaxValue, CancellationToken.None)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Publish_then_GetCurrent_returns_published_row()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow v1 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        WorkflowDefinitionRow v2 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        WorkflowDefinitionRow current = (await provider.FindCurrentByRefIdAsync(refId, CancellationToken.None))!;

        current.Version.Should().Be(v2.Version).And.BeGreaterThan(v1.Version);
        current.RefId.Should().Be(refId);
    }

    [SkippableFact]
    public async Task A_first_publish_is_disabled_with_no_live_version()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        WorkflowStateRow? state = await ProviderFactory.WorkflowState(fixture.ConnectionString).FindAsync(refId, inserted.WorkspaceId, CancellationToken.None);
        state.Should().NotBeNull();
        state!.State.Should().Be(WorkflowState.Disabled);
        state.LiveVersion.Should().BeNull();
        state.LatestVersion.Should().Be(inserted.Version);
    }

    [SkippableFact]
    public async Task Duplicate_Insert_same_RefId_increments_version()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow v1 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        WorkflowDefinitionRow v2 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        v2.Version.Should().Be(v1.Version + 1);
    }

    [SkippableFact]
    public async Task FindByRefIdVersionAsync_returns_id_for_existing_row()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        int? id = await provider.FindByRefIdVersionAsync(refId, inserted.Version, CancellationToken.None);

        id.Should().Be(inserted.Id);
    }

    [SkippableFact]
    public async Task FindByRefIdVersionAsync_returns_null_for_missing_row()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);

        int? id = await provider.FindByRefIdVersionAsync(Guid.NewGuid(), 999, CancellationToken.None);

        id.Should().BeNull();
    }
}
