using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Common.Services;

namespace Wbskt.Management.Api.Controllers;

[Authorize]
[ApiController]
public class WorkflowExecutionsController : ControllerBase
{
    private readonly IWorkflowExecutionsReader _executionsReader;
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowStepExecutionsReader _stepExecutionsReader;
    private readonly ICurrentUser _currentUser;

    public WorkflowExecutionsController(
        IWorkflowExecutionsReader executionsReader,
        IWorkflowsReader workflowsReader,
        IWorkflowStepExecutionsReader stepExecutionsReader,
        ICurrentUser currentUser)
    {
        _executionsReader = executionsReader;
        _workflowsReader = workflowsReader;
        _stepExecutionsReader = stepExecutionsReader;
        _currentUser = currentUser;
    }

    [HttpGet("api/workflows/{refId}/executions")]
    public async Task<IActionResult> GetExecutionsForWorkflow(Guid refId, CancellationToken cancellationToken)
    {
        var workflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (workflow == null || workflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        var executions = await _executionsReader.GetAllForWorkflowAsync(refId, cancellationToken);
        return Ok(executions);
    }

    [HttpGet("api/executions/{executionId}")]
    public async Task<IActionResult> GetExecution(int executionId, CancellationToken cancellationToken)
    {
        var execution = await _executionsReader.GetByIdAsync(executionId, cancellationToken);
        if (execution == null)
        {
            return NotFound();
        }

        // Security check: ensure the user owns the workflow this execution belongs to
        var workflow = await _workflowsReader.GetByRefIdAsync(execution.WorkflowRefId, cancellationToken);
        if (workflow == null || workflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        var steps = await _stepExecutionsReader.GetAllForExecutionAsync(executionId, cancellationToken);

        return Ok(new { execution, steps });
    }
}
