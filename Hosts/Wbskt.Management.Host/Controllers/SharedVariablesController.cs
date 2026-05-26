using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workflows/{workflowRefId:guid}/variables")]
[ApiController]
public sealed class SharedVariablesController(ISharedVariableProvider variableProvider) : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<SharedVariableDto> Get(Guid workflowRefId, string name, CancellationToken ct)
    {
        SharedVariableRow row = await variableProvider.GetByWorkflowRefIdNameAsync(workflowRefId, name, ct);
        return Map(row);
    }

    [HttpPut("{name}")]
    public async Task<SharedVariableDto> Set(Guid workflowRefId, string name, [FromBody] SharedVariableSetRequest request, CancellationToken ct)
    {
        SharedVariableRow row = await variableProvider.SetAsync(workflowRefId, name, request.ValueJson, ct);
        return Map(row);
    }

    private static SharedVariableDto Map(SharedVariableRow row)
    {
        return new SharedVariableDto(row.WorkflowRefId, row.VarName, row.VarType, row.ValueJson, row.UpdatedAt);
    }
}
