using System.Text.Json;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Management;
using Webskt.Management.Host.Models;
using Webskt.Workflow.Providers;
using Webskt.Workflow.Mappers;
using Webskt.Workflow.Abstraction.Models;

namespace Webskt.Management.Host.Services;

internal sealed class WorkflowService : IWorkflowService
{
    private readonly IWorkflowProvider _provider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowProvider provider, 
        IEventBus eventBus,
        ILogger<WorkflowService> logger)
    {
        _provider = provider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<WorkflowSummaryResponse>> GetAllAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        var workflows = await _provider.GetAllByWorkspaceAsync(workspaceId, cancellationToken);
        
        return workflows.Select(w => new WorkflowSummaryResponse(
            w.RefId,
            w.Name,
            w.Description,
            w.IsEnabled,
            w.CreatedAt
        )).ToList().AsReadOnly();
    }

    public async Task<WorkflowDefinition> GetByIdAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        var workflow = await _provider.GetByIdAsync(id, cancellationToken);

        if (workflow.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Access denied to workflow.");
        }

        var definition = workflow.ToDefinition();
        if (definition == null)
        {
            _logger.LogError("Invalid JSON in database for workflow {Id}", id);
            throw new InternalServerException("Workflow definition is corrupted or invalid.");
        }

        return definition;
    }

    public async Task<WorkflowSummaryResponse> CreateAsync(int workspaceId, CreateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Workflow name is required.");
        }

        var workflow = await _provider.InsertAsync(workspaceId, request.Name, request.Description, cancellationToken);
        
        await _eventBus.PublishAsync(new WorkflowCreatedEvent(workflow.RefId, workspaceId), cancellationToken);

        return new WorkflowSummaryResponse(
            workflow.RefId,
            workflow.Name,
            workflow.Description,
            workflow.IsEnabled,
            workflow.CreatedAt
        );
    }

    public async Task UpdateAsync(int workspaceId, int id, UpdateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _provider.GetByIdAsync(id, cancellationToken);

        if (existing.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Access denied to workflow.");
        }

        // 1. Create the new definition object
        var definition = new WorkflowDefinition
        {
            WorkflowRefId = existing.RefId,
            WorkspaceId = workspaceId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            IsEnabled = request.IsEnabled,
            Version = existing.Version + 1,
            Nodes = request.Nodes,
            Edges = request.Edges,
            InitialState = request.InitialState
        };

        // 2. Serialize to JSON
        var json = JsonSerializer.Serialize(definition);

        // 3. Save to database
        await _provider.UpdateAsync(
            id, 
            request.Name, 
            request.Description ?? string.Empty, 
            request.IsEnabled, 
            json, 
            cancellationToken);

        // 4. Notify the Engine to reload
        await _eventBus.PublishAsync(new WorkflowUpdatedEvent(existing.RefId, workspaceId), cancellationToken);
    }

    public async Task DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        var existing = await _provider.GetByIdAsync(id, cancellationToken);

        if (existing.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Access denied to workflow.");
        }

        await _provider.DeleteAsync(id, cancellationToken);

        await _eventBus.PublishAsync(new WorkflowDeletedEvent(existing.RefId, workspaceId), cancellationToken);
    }
}