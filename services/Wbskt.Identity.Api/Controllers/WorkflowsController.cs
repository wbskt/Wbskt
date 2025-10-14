using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;

namespace Wbskt.Identity.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowsWriter _workflowsWriter;
    private readonly IWorkflowStepsReader _workflowStepsReader;
    private readonly IWorkflowStepsWriter _workflowStepsWriter;
    private readonly ICurrentUser _currentUser;

    public WorkflowsController(
        IWorkflowsReader workflowsReader,
        IWorkflowsWriter workflowsWriter,
        IWorkflowStepsReader workflowStepsReader,
        IWorkflowStepsWriter workflowStepsWriter,
        ICurrentUser currentUser)
    {
        _workflowsReader = workflowsReader;
        _workflowsWriter = workflowsWriter;
        _workflowStepsReader = workflowStepsReader;
        _workflowStepsWriter = workflowStepsWriter;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetWorkflows(CancellationToken cancellationToken)
    {
        var workflows = await _workflowsReader.GetAllForUserAsync(_currentUser.Id, cancellationToken);
        return Ok(workflows);
    }

    [HttpPost]
    public async Task<IActionResult> CreateWorkflow([FromBody] WorkflowRecord workflow, CancellationToken cancellationToken)
    {
        if (workflow.TriggerType == "Webhook")
        {
            // Only generate a webhookId if one isn't provided in the configuration.
            // This part would need a proper JSON parsing/merging utility in a real implementation.
            if (string.IsNullOrWhiteSpace(workflow.TriggerConfiguration) || !workflow.TriggerConfiguration.Contains("webhookId"))
            {
                var webhookId = Guid.NewGuid();
                workflow = workflow with { TriggerConfiguration = $"{{\"webhookId\":\"{webhookId}\"}}" };
            }
        }

        workflow = workflow with { UserId = _currentUser.Id };
        var workflowId = await _workflowsWriter.CreateAsync(workflow, cancellationToken);
        return CreatedAtAction(nameof(GetWorkflow), new { refId = workflow.RefId }, workflow with { Id = workflowId });
    }

    [HttpGet("{refId}")]
    public async Task<IActionResult> GetWorkflow(Guid refId, CancellationToken cancellationToken)
    {
        var workflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (workflow == null || workflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        var steps = await _workflowStepsReader.GetAllForWorkflowAsync(workflow.Id, cancellationToken);

        return Ok(new { workflow, steps });
    }

    [HttpPut("{refId}")]
    public async Task<IActionResult> UpdateWorkflow(Guid refId, [FromBody] WorkflowRecord workflow, CancellationToken cancellationToken)
    {
        var existingWorkflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (existingWorkflow == null || existingWorkflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        workflow = workflow with { Id = existingWorkflow.Id, UserId = _currentUser.Id };
        await _workflowsWriter.UpdateAsync(workflow, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{refId}")]
    public async Task<IActionResult> DeleteWorkflow(Guid refId, CancellationToken cancellationToken)
    {
        var existingWorkflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (existingWorkflow == null || existingWorkflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        await _workflowsWriter.DeleteAsync(refId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{refId}/steps")]
    public async Task<IActionResult> UpdateWorkflowSteps(Guid refId, [FromBody] IEnumerable<WorkflowStepRecord> steps, CancellationToken cancellationToken)
    {
        var existingWorkflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (existingWorkflow == null || existingWorkflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        await _workflowStepsWriter.BulkUpdateForWorkflowAsync(existingWorkflow.Id, steps, cancellationToken);
        return NoContent();
    }
}
