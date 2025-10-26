using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Enums;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;
using Wbskt.Identity.Api.Contracts;

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
    public async Task<IActionResult> CreateWorkflow([FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var workflow = new WorkflowRecord
        {
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            TriggerType = request.TriggerType,
            TriggerConfiguration = request.TriggerConfiguration,
            ViewportX = request.ViewportX,
            ViewportY = request.ViewportY,
            ViewportZoom = request.ViewportZoom,
            UserId = _currentUser.Id,
            RefId = Guid.NewGuid(),
            LastModified = DateTime.UtcNow
        };

        if (request.TriggerType == TriggerType.Webhook)
        {
            // Only generate a webhookId if one isn't provided in the configuration.
            // This part would need a proper JSON parsing/merging utility in a real implementation.
            if (string.IsNullOrWhiteSpace(workflow.TriggerConfiguration) || !workflow.TriggerConfiguration.Contains("webhookId"))
            {
                var webhookId = Guid.NewGuid();
                workflow = workflow with { TriggerConfiguration = $"{{\"webhookId\":\"{webhookId}\"}}" };
            }
        }

        var workflowId = await _workflowsWriter.CreateAsync(workflow, cancellationToken);
        await _workflowStepsWriter.BulkUpdateForWorkflowAsync(workflowId, request.Steps, cancellationToken);

        var response = new WorkflowDetailResponse
        {
            RefId = workflow.RefId,
            Name = workflow.Name,
            Description = workflow.Description,
            IsEnabled = workflow.IsEnabled,
            TriggerType = workflow.TriggerType,
            TriggerConfiguration = workflow.TriggerConfiguration,
            LastModified = workflow.LastModified,
            Steps = request.Steps
        };

        return CreatedAtAction(nameof(GetWorkflow), new { refId = workflow.RefId }, response);
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

        var response = new WorkflowDetailResponse
        {
            RefId = workflow.RefId,
            Name = workflow.Name,
            Description = workflow.Description,
            IsEnabled = workflow.IsEnabled,
            TriggerType = workflow.TriggerType,
            TriggerConfiguration = workflow.TriggerConfiguration,
            LastModified = workflow.LastModified,
            ViewportX = workflow.ViewportX,
            ViewportY = workflow.ViewportY,
            ViewportZoom = workflow.ViewportZoom,
            Steps = steps
        };

        return Ok(response);
    }

    [HttpPut("{refId}")]
    public async Task<IActionResult> UpdateWorkflow(Guid refId, [FromBody] UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var existingWorkflow = await _workflowsReader.GetByRefIdAsync(refId, cancellationToken);
        if (existingWorkflow == null || existingWorkflow.UserId != _currentUser.Id)
        {
            return NotFound();
        }

        var workflow = new WorkflowRecord
        {
            Id = existingWorkflow.Id,
            RefId = refId,
            UserId = _currentUser.Id,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            TriggerType = request.TriggerType,
            TriggerConfiguration = request.TriggerConfiguration,
            ViewportX = request.ViewportX,
            ViewportY = request.ViewportY,
            ViewportZoom = request.ViewportZoom,
            LastModified = DateTime.UtcNow
        };

        await _workflowsWriter.UpdateAsync(workflow, cancellationToken);
        await _workflowStepsWriter.BulkUpdateForWorkflowAsync(existingWorkflow.Id, request.Steps, cancellationToken);

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
}
