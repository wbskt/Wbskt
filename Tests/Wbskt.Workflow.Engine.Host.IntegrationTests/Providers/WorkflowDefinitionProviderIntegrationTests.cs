using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>Task 13.2 — WorkflowDefinitionProvider: insert, retrieve, deprecate round-trips.</summary>
[Collection("SqlEdge")]
public sealed class WorkflowDefinitionProviderIntegrationTests(SqlEdgeFixture fixture, ITestOutputHelper output)
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

    [Fact]
    public async Task Insert_then_GetByRefIdVersion_round_trips()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        inserted.RefId.Should().Be(refId);
        inserted.Version.Should().BeGreaterThan(0);
        inserted.Name.Should().StartWith("IT-Workflow-");

        WorkflowDefinitionRow retrieved = await provider.GetByRefIdVersionAsync(refId, inserted.Version, CancellationToken.None);

        retrieved.Id.Should().Be(inserted.Id);
        retrieved.RefId.Should().Be(refId);
        retrieved.DefinitionJson.Should().Be("""{"nodes":[],"edges":[]}""");
    }

    [Fact]
    public async Task Publish_then_GetCurrent_returns_published_row()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow v1 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        WorkflowDefinitionRow v2 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        WorkflowDefinitionRow current = await provider.GetCurrentByRefIdAsync(refId, CancellationToken.None);

        current.Version.Should().Be(v2.Version).And.BeGreaterThan(v1.Version);
        current.RefId.Should().Be(refId);
    }

    [Fact]
    public async Task Deprecate_changes_IsEnabled_to_false()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        inserted.IsEnabled.Should().BeTrue();

        await provider.DeprecateAsync(inserted.Id, CancellationToken.None);

        WorkflowDefinitionRow retrieved = await provider.GetByIdAsync(inserted.Id, CancellationToken.None);
        retrieved.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_Insert_same_RefId_increments_version()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow v1 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);
        WorkflowDefinitionRow v2 = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        v2.Version.Should().Be(v1.Version + 1);
    }

    [Fact]
    public async Task FindByRefIdVersionAsync_returns_id_for_existing_row()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid refId = Guid.NewGuid();

        WorkflowDefinitionRow inserted = await provider.InsertAsync(BuildRow(refId), CancellationToken.None);

        int? id = await provider.FindByRefIdVersionAsync(refId, inserted.Version, CancellationToken.None);

        id.Should().Be(inserted.Id);
    }

    [Fact]
    public async Task FindByRefIdVersionAsync_returns_null_for_missing_row()
    {
        if (!fixture.IsAvailable) { output.WriteLine("SKIPPED: SQL Edge not available."); return; }

        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);

        int? id = await provider.FindByRefIdVersionAsync(Guid.NewGuid(), 999, CancellationToken.None);

        id.Should().BeNull();
    }
}
