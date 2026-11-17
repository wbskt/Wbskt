namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IClientCommandPublisher
{
    /// <summary>
    /// Sends <paramref name="command"/> to a client. <paramref name="sentBy"/> names the workflow run
    /// sending it, so the audit log shows the run, not a blank, as who did it. The client's
    /// acknowledgement, or the report that the command failed, carries <paramref name="commandId"/>.
    /// The client refuses the command once <paramref name="expiresIn"/> has passed, so it is never
    /// acted on after the step sending it has stopped waiting. A command that can be neither sent
    /// nor queued (an unknown or inactive client, a full queue, an invalid command or expiry) is
    /// reported as failed and answered as refused; a broker that is down is thrown.
    /// </summary>
    Task<CommandPublishOutcome> PublishCommandAsync(Guid commandId, Guid clientRefId, int workspaceId, string command, string payload, TimeSpan expiresIn, CommandSender? sentBy, CancellationToken ct);
}

/// <summary>The workflow and run a command came from.</summary>
public sealed record CommandSender(Guid WorkflowRefId, Guid RunRefId);

/// <summary>Whether a command was sent or queued for its client, and why not when it was refused.</summary>
public sealed record CommandPublishOutcome(bool Accepted, string? RefusedReason)
{
    public static readonly CommandPublishOutcome Sent = new(true, null);

    public static CommandPublishOutcome Refused(string reason) => new(false, reason);
}
