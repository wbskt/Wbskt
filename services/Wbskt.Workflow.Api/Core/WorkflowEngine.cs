using System.Text.Json;
using Wbskt.Common.Configurations;
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
    private readonly IWorkflowStepExecutionsWriter _stepExecutionsWriter;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IServiceProvider serviceProvider,
        IWorkflowsReader workflowsReader,
        IWorkflowStepsReader workflowStepsReader,
        IWorkflowExecutionsWriter executionsWriter,
        IWorkflowStepExecutionsWriter stepExecutionsWriter,
        ILogger<WorkflowEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _workflowsReader = workflowsReader;
        _workflowStepsReader = workflowStepsReader;
        _executionsWriter = executionsWriter;
        _stepExecutionsWriter = stepExecutionsWriter;
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
            WorkflowRefId = workflow.RefId,
            Status = "Running",
            TriggeredAt = DateTime.UtcNow,
            InitialContext = JsonSerializer.Serialize(initialContext.Properties)
        };

        var executionId = await _executionsWriter.CreateAsync(executionRecord, CancellationToken.None);
        var context = new WorkflowContext(Guid.NewGuid(), initialContext.Properties);

        try
        {
            var steps = (await _workflowStepsReader.GetAllForWorkflowAsync(workflow.Id, CancellationToken.None))
                .OrderBy(s => s.StepOrder)
                .ToList();

            if (!steps.Any()) return;

            var stepMap = steps.ToDictionary(s => s.Id);
            int? currentStepId = steps.First().Id;

            while (currentStepId.HasValue)
            {
                var step = stepMap[currentStepId.Value];
                var stepExecutionRecord = new WorkflowStepExecutionRecord
                {
                    WorkflowExecutionId = executionId,
                    WorkflowStepId = step.Id,
                    Status = "Running",
                    StartedAt = DateTime.UtcNow,
                    InputContext = JsonSerializer.Serialize(context.Properties)
                };
                var stepExecutionId = await _stepExecutionsWriter.CreateAsync(stepExecutionRecord, CancellationToken.None);

                try
                {
                    _logger.LogInformation("Executing step {StepName} ({StepIdentifier})", step.Name, step.StepIdentifier);

                    using var scope = _serviceProvider.CreateScope();
                    var action = scope.ServiceProvider.GetRequiredKeyedService<IAction>(step.StepIdentifier);
    
                    // todo: custom mapping needed
                    var configuration = JsonSerializer.Deserialize<StepConfigurationBase>(step.StepConfiguration ?? "{}");
    
                    if (configuration == null)
                    {
                        throw new InvalidOperationException($"Could not deserialize configuration for step '{step.Name}'.");
                    }
    
                    var result = await action.ExecuteAsync( configuration, context, CancellationToken.None);
                    stepExecutionRecord = stepExecutionRecord with
                    {
                        Id = stepExecutionId,
                        Status = "Success",
                        CompletedAt = DateTime.UtcNow,
                        OutputContext = JsonSerializer.Serialize(context.Properties)
                    };
                    await _stepExecutionsWriter.UpdateAsync(stepExecutionRecord, CancellationToken.None);

                    if (result.IsSuccess)
                    {
                        currentStepId = step.OnSuccessStepId;
                        if (!currentStepId.HasValue) // If no explicit success path, try to go to next in order
                        {
                            var nextStep = steps.FirstOrDefault(s => s.StepOrder > step.StepOrder);
                            currentStepId = nextStep?.Id;
                        }
                    }
                    else
                    {
                        currentStepId = step.OnFailureStepId;
                        // If no explicit failure path, the workflow branch terminates.
                    }
                }
                catch (Exception ex)
                {
                    stepExecutionRecord = stepExecutionRecord with
                    {
                        Id = stepExecutionId,
                        Status = "Failed",
                        CompletedAt = DateTime.UtcNow,
                        ErrorLog = ex.Message
                    };
                    await _stepExecutionsWriter.UpdateAsync(stepExecutionRecord, CancellationToken.None);
                    throw; // Re-throw to fail the main workflow execution
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
