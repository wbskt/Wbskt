namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IDeviceCommandPublisher
{
    /// <summary>
    /// Sends <paramref name="command"/> to a device. <paramref name="sentBy"/> names the workflow run
    /// sending it, so the audit log shows the run, not a blank, as who did it.
    /// </summary>
    Task PublishCommandAsync(Guid clientRefId, int workspaceId, string command, string payload, CommandSender? sentBy, CancellationToken ct);
}

/// <summary>The workflow and run a command came from.</summary>
public sealed record CommandSender(Guid WorkflowRefId, Guid RunRefId);
