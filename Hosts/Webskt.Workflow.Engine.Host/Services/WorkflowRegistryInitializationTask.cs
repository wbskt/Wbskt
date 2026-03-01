using Webskt.Common.Abstraction.Interfaces;
using Webskt.Workflow.Engine.Host.Interfaces;

namespace Webskt.Workflow.Engine.Host.Services;

public sealed class WorkflowRegistryInitializationTask : IStartupTask
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowDefinitionProvider _provider;
    private readonly ILogger<WorkflowRegistryInitializationTask> _logger;

    public WorkflowRegistryInitializationTask(
        IWorkflowRuntimeRegistry registry,
        IWorkflowDefinitionProvider provider,
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
            var definitions = await _provider.GetAllEnabledAsync();
            
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
