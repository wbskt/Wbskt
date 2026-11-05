namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IClientCommandPublisher
{
    /// <summary>
    /// Sends <paramref name="command"/> to a client. <paramref name="sentBy"/> names the workflow run
    /// sending it, so the audit log shows the run, not a blank, as who did it. The client's
    /// acknowledgement, or the report that the command failed, carries <paramref name="commandId"/>.
    /// The client refuses the command once <paramref name="expiresIn"/> has passed, so it is never
    /// acted on after the step sending it has stopped waiting.
    /// </summary>
    Task PublishCommandAsync(Guid commandId, Guid clientRefId, int workspaceId, string command, string payload, TimeSpan expiresIn, CommandSender? sentBy, CancellationToken ct);
}

/// <summary>The workflow and run a command came from.</summary>
public sealed record CommandSender(Guid WorkflowRefId, Guid RunRefId);
