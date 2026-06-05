namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IDeviceCommandPublisher
{
    Task PublishCommandAsync(Guid clientRefId, int clientId, int workspaceId, string command, string payload, CancellationToken ct);
}
