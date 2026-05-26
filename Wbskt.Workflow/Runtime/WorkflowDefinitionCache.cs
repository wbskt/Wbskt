using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class WorkflowDefinitionCache : IWorkflowDefinitionCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(10);
    private readonly IMemoryCache _cache;
    private readonly IWorkflowDefinitionProvider _provider;

    public WorkflowDefinitionCache(IMemoryCache cache, IWorkflowDefinitionProvider provider)
    {
        _cache = cache;
        _provider = provider;
    }

    public async Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct)
    {
        if (_cache.TryGetValue(workflowDefinitionId, out WorkflowDefinition? cached) && cached is not null)
        {
            return cached;
        }

        var row = await _provider.GetByIdAsync(workflowDefinitionId, ct);
        if (row is null)
        {
            throw new KeyNotFoundException($"Workflow definition {workflowDefinitionId} was not found.");
        }

        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} could not be deserialized.");

        _cache.Set(workflowDefinitionId, definition, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration
        });

        return definition;
    }

    public void Invalidate(int workflowDefinitionId)
    {
        _cache.Remove(workflowDefinitionId);
    }
}
