namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Raises a workspace-addressed toast. Mirrors <see cref="IDeviceCommandPublisher"/>: the abstraction
/// lives here so <c>Wbskt.Workflow</c> takes no dependency on the event bus, and the engine host
/// supplies the implementation.
/// </summary>
public interface IToastPublisher
{
    Task PublishToastAsync(
        Guid workflowRefId,
        int workflowDefinitionId,
        Guid runRefId,
        int workspaceId,
        string title,
        string message,
        CancellationToken ct);
}
