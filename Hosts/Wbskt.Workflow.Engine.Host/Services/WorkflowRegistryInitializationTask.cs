using Wbskt.Primitives;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Mappers;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.Services;

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
            var definitions = new List<WorkflowDefinition>(entities.Count);

            foreach (var entity in entities)
            {
                var definition = entity.ToDefinition();
                if (definition != null)
                {
                    definitions.Add(definition);
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize workflow {WorkflowRefId}. Skipping.", entity.RefId);
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
