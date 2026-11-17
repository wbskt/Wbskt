namespace Wbskt.Management.Models.Workflow;

/// <summary>Stops new runs. Runs already going finish, or are cancelled when <c>CancelRunningRuns</c>.</summary>
public record WorkflowDisableRequest(bool CancelRunningRuns = false);
