using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/workflows/{workflowRefId:guid}/variables")]
[ApiController]
[Authorize]
public sealed class SharedVariablesController(
    ISharedVariableProvider variableProvider,
    IWorkflowDefinitionService workflowService,
    IAuthServiceClient authClient) : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<SharedVariableDto> Get(Guid workspaceRef, Guid workflowRefId, string name, CancellationToken ct)
    {
        int workspaceId = await authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        await workflowService.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);

        SharedVariableRow row = await variableProvider.GetByWorkflowRefIdNameAsync(workflowRefId, name, ct);
        return Map(row);
    }

    [HttpPut("{name}")]
    public async Task<SharedVariableDto> Set(Guid workspaceRef, Guid workflowRefId, string name, [FromBody] SharedVariableSetRequest request, CancellationToken ct)
    {
        int workspaceId = await authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);
        await workflowService.EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);

        SharedVariableRow row = await variableProvider.SetAsync(workflowRefId, name, request.ValueJson, ct);
        return Map(row);
    }

    private static SharedVariableDto Map(SharedVariableRow row)
    {
        return new SharedVariableDto(row.WorkflowRefId, row.VarName, row.VarType, row.ValueJson, row.UpdatedAt);
    }
}
