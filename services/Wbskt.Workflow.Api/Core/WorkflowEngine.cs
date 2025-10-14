using System.Text.Json;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Core;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowStepsReader _workflowStepsReader;
    private readonly IWorkflowExecutionsWriter _executionsWriter;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IServiceProvider serviceProvider,
        IWorkflowsReader workflowsReader,
        IWorkflowStepsReader workflowStepsReader,
        IWorkflowExecutionsWriter executionsWriter,
        ILogger<WorkflowEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _workflowsReader = workflowsReader;
        _workflowStepsReader = workflowStepsReader;
        _executionsWriter = executionsWriter;
        _logger = logger;
    }

    public async Task ExecuteWorkflowAsync(Guid workflowRefId, WorkflowContext initialContext)
    {
        _logger.LogInformation("Starting workflow execution for {WorkflowRefId}", workflowRefId);

        var workflow = await _workflowsReader.GetByRefIdAsync(workflowRefId, CancellationToken.None);
        if (workflow == null || !workflow.IsEnabled)
        {
            _logger.LogWarning("Workflow {WorkflowRefId} not found or is disabled.", workflowRefId);
            return;
        }

        var executionRecord = new WorkflowExecutionRecord
        {
            WorkflowId = workflow.Id,
            Status = "Running",
            TriggeredAt = DateTime.UtcNow,
            InitialContext = JsonSerializer.Serialize(initialContext.Properties)
        };

        var executionId = await _executionsWriter.CreateAsync(executionRecord, CancellationToken.None);
        var context = new WorkflowContext(Guid.NewGuid(), initialContext.Properties);

        try
        {
            var steps = await _workflowStepsReader.GetAllForWorkflowAsync(workflow.Id, CancellationToken.None);

            foreach (var step in steps.OrderBy(s => s.StepOrder))
            {
                _logger.LogInformation("Executing step {StepName} ({StepIdentifier})", step.Name, step.StepIdentifier);

                using var scope = _serviceProvider.CreateScope();
                var action = scope.ServiceProvider.GetRequiredService(Type.GetType($"Wbskt.Workflow.Api.Actions.{step.StepIdentifier}")) as IAction;

                if (action == null)
                {
                    throw new InvalidOperationException($"Action with identifier '{step.StepIdentifier}' not found.");
                }

                var result = await action.ExecuteAsync(context, CancellationToken.None);
                if (!result.IsSuccess)
                {
                    throw new Exception(result.ErrorMessage);
                }
            }

            executionRecord = executionRecord with { Id = executionId, Status = "Success", CompletedAt = DateTime.UtcNow };
            await _executionsWriter.UpdateAsync(executionRecord, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed for {WorkflowRefId}", workflowRefId);
            executionRecord = executionRecord with { Id = executionId, Status = "Failed", CompletedAt = DateTime.UtcNow, ErrorLog = ex.Message };
            await _executionsWriter.UpdateAsync(executionRecord, CancellationToken.None);
        }
    }
}
