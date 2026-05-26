namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ITriggerRegistrationService
{
    Task OnPublishedAsync(int workflowDefinitionId, CancellationToken ct);
    Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct);
}
