using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class WorkflowDefinitionCacheTests
{
    [Fact]
    public async Task Get_loads_from_provider_on_miss()
    {
        // Arrange
        var provider = new RecordingWorkflowDefinitionProvider(CreateRow("first"));
        var cache = new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), provider);

        // Act
        var definition = await cache.GetAsync(42, CancellationToken.None);

        // Assert
        Assert.Equal("first", definition.Name);
        Assert.Equal([42], provider.RequestedIds);
    }

    [Fact]
    public async Task Get_returns_cached_on_hit()
    {
        // Arrange
        var provider = new RecordingWorkflowDefinitionProvider(CreateRow("cached"));
        var cache = new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), provider);

        // Act
        var first = await cache.GetAsync(42, CancellationToken.None);
        var second = await cache.GetAsync(42, CancellationToken.None);

        // Assert
        Assert.Same(first, second);
        Assert.Equal([42], provider.RequestedIds);
    }

    [Fact]
    public async Task Invalidate_forces_reload()
    {
        // Arrange
        var provider = new RecordingWorkflowDefinitionProvider(CreateRow("before"), CreateRow("after"));
        var cache = new WorkflowDefinitionCache(new MemoryCache(new MemoryCacheOptions()), provider);

        // Act
        var first = await cache.GetAsync(42, CancellationToken.None);
        cache.Invalidate(42);
        var second = await cache.GetAsync(42, CancellationToken.None);

        // Assert
        Assert.Equal("before", first.Name);
        Assert.Equal("after", second.Name);
        Assert.Equal([42, 42], provider.RequestedIds);
    }

    private static WorkflowDefinitionRow CreateRow(string name)
    {
        WorkflowDefinition definition = new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            1,
            99,
            name,
            null,
            true,
            [
                new ManualTriggerNode(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "start",
                    [new PortDefinition("out", PortDirection.Output, "Out")],
                    new ManualTriggerConfig()),
                new LogicGateNode(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "logic",
                    [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("next", PortDirection.Output, "Next")],
                    new LogicGateConfig("true"))
            ],
            [
                new Edge(
                    (Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "out"),
                    (Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "in"))
            ],
            [],
            DateTime.UtcNow,
            7);

        return new WorkflowDefinitionRow
        {
            Id = 42,
            RefId = definition.WorkflowRefId,
            Version = definition.Version,
            WorkspaceId = definition.WorkspaceId,
            Name = definition.Name,
            Description = definition.Description,
            IsEnabled = definition.IsEnabled,
            DefinitionJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            PublishedBy = definition.PublishedBy,
            CreatedAt = definition.CreatedAt
        };
    }

    private sealed class RecordingWorkflowDefinitionProvider(params WorkflowDefinitionRow[] rows) : IWorkflowDefinitionProvider
    {
        private readonly Queue<WorkflowDefinitionRow> _rows = new(rows);

        public List<int> RequestedIds { get; } = [];

        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
        {
            RequestedIds.Add(id);
            if (_rows.Count == 0)
            {
                return Task.FromResult<WorkflowDefinitionRow>(null!);
            }

            return Task.FromResult(_rows.Dequeue());
        }

        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task DeprecateAsync(int id, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }
}
