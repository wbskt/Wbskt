namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ITriggerRegistrationService
{
    // workspaceRef scopes webhook trigger keys so an author-chosen path (e.g. "orders") is unique
    // per workspace and cannot collide with, or be triggered through, another workspace's webhook.
    Task OnPublishedAsync(int workflowDefinitionId, Guid workspaceRef, CancellationToken ct);
    Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct);
}
