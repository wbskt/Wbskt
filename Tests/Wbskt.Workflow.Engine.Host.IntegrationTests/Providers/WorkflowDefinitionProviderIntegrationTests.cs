using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
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
        IsEnabled = true,
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

        WorkflowDefinitionRow retrieved = await provider.GetByRefIdVersionAsync(refId, inserted.Version, CancellationToken.None);

        retrieved.Id.Should().Be(inserted.Id);
        retrieved.RefId.Should().Be(refId);

        // Not compared byte-for-byte with the input: WorkflowDefinition_Publish is the single
        // authority on the version number and stamps it into the JSON under the same HOLDLOCK that
        // computed it, alongside isEnabled. So the stored document is the submitted one plus those
        // two properties, and asserting equality with the input would be asserting that the stamping
        // does not happen.
        using JsonDocument stored = JsonDocument.Parse(retrieved.DefinitionJson);
        JsonElement root = stored.RootElement;

        root.GetProperty("nodes").GetArrayLength().Should().Be(0);
        root.GetProperty("edges").GetArrayLength().Should().Be(0);
        root.GetProperty("version").GetInt32().Should().Be(inserted.Version);
        root.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
    }

    [SkippableFact]
    public async Task Publish_then_GetCurrent_returns_published_row()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow v1 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        WorkflowDefinitionRow v2 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        WorkflowDefinitionRow current = await provider.GetCurrentByRefIdAsync(refId, CancellationToken.None);

        current.Version.Should().Be(v2.Version).And.BeGreaterThan(v1.Version);
        current.RefId.Should().Be(refId);
    }

    [SkippableFact]
    public async Task Deprecate_changes_IsEnabled_to_false()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        inserted.IsEnabled.Should().BeTrue();

        await provider.DeprecateAsync(inserted.Id, CancellationToken.None);

        WorkflowDefinitionRow retrieved = await provider.GetByIdAsync(inserted.Id, CancellationToken.None);
        retrieved.IsEnabled.Should().BeFalse();
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
