using System.Text.Json;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Providers;

namespace Webskt.Workflow.Engine.Host.Services;

public sealed class WorkflowRegistryInitializationTask : IStartupTask
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowProvider _provider;
    private readonly ILogger<WorkflowRegistryInitializationTask> _logger;

    public WorkflowRegistryInitializationTask(
        IWorkflowRuntimeRegistry registry,
        IWorkflowProvider provider,
        ILogger<WorkflowRegistryInitializationTask> logger)
    {
        _registry = registry;
        _provider = provider;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Workflow Runtime Registry initialization...");

        try
        {
            var entities = await _provider.GetAllEnabledAsync(cancellationToken);
            var definitions = new List<WorkflowDefinition>();

            foreach (var entity in entities)
            {
                try
                {
                    var definition = JsonSerializer.Deserialize<WorkflowDefinition>(entity.DefinitionJson);
                    if (definition != null)
                    {
                        // Sync DB-level fields
                        definition.WorkflowRefId = entity.RefId;
                        definition.WorkspaceId = entity.WorkspaceId;
                        definition.Name = entity.Name;
                        definition.Description = entity.Description;
                        definition.IsEnabled = entity.IsEnabled;
                        definition.Version = entity.Version;
                        definition.CreatedAt = entity.CreatedAt;

                        definitions.Add(definition);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize workflow {WorkflowRefId}. Skipping.", entity.RefId);
                }
            }
            
            _registry.Initialize(definitions);

            _logger.LogInformation("Successfully initialized Workflow Runtime Registry with {Count} workflows.", definitions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Workflow Runtime Registry from the database.");
            throw; // Critical failure, stop the application startup
        }
    }
}
