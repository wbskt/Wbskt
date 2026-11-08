using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>Test runs: a workflow's draft or live version run by hand, whatever its trigger.</summary>
public interface IWorkflowTestRunService
{
    Task<Result<StartTestRunResponse>> StartAsync(int workspaceId, Guid workflowRefId, StartTestRunRequest request, CancellationToken ct);
}
