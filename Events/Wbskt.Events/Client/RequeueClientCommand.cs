using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

/// <summary>
/// A command to the management host: the socket host that was to deliver this command found the
/// device's connection gone, so put it back in the device's queue to wait for the next connect. The
/// socket host keeps no state of its own, so it hands the whole command back, with who sent it.
/// Consumed from one shared queue, so exactly one management instance queues it.
/// <see cref="ReturnedByHostId"/> is the socket host handing it back, so the management host doesn't
/// deliver it straight back there while presence still names that host.
/// </summary>
public sealed record RequeueClientCommand(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string Type,
    string Payload,
    Guid CommandId,
    DateTime? ExpiresAtUtc,
    int? ActorUserId,
    Guid? ActorUserRefId,
    EventSource? ActorSource,
    Guid? ActorWorkflowRefId,
    Guid? ActorRunRefId,
    string? ReturnedByHostId = null) : ICommand
{
    public static string QueueName => "client-command-requeue";
}
