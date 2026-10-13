using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// A workflow's command, sent by the same <see cref="IDeviceCommandSender"/> as the console's. A
/// command that is not sent is reported at once as a <see cref="ClientCommandFailedEvent"/>, which
/// takes a waiting step out by its "failed" port, rather than dropped unseen. A broker that is down
/// is thrown, for the step to fail and the run to retry.
/// </summary>
/// <remarks>
/// A device that reads as offline is looked up again a few times over a couple of seconds before the
/// command is reported failed. Presence is recorded by the management host from the socket host's
/// connected event, while the device's own messages reach the engine directly, so a run started by a
/// device's first message can get here before its connection is recorded (#141). The console's
/// sends answer at once: a person is not racing the device's first message.
/// </remarks>
/// <param name="offlineRechecks">The waits before each re-check of an offline device; tests shorten them.</param>
public sealed class DeviceCommandPublisher(
    IDeviceCommandSender sender,
    IEventBus eventBus,
    TimeProvider clock,
    IReadOnlyList<TimeSpan>? offlineRechecks = null) : IDeviceCommandPublisher
{
    internal const string UnknownClientReason = "The device does not exist in this workspace.";

    /// <summary>About two and a half seconds in all, well past the few hundred milliseconds presence takes to land.</summary>
    internal static readonly IReadOnlyList<TimeSpan> DefaultOfflineRechecks =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(1)];

    private readonly IReadOnlyList<TimeSpan> _offlineRechecks = offlineRechecks ?? DefaultOfflineRechecks;

    public async Task PublishCommandAsync(Guid commandId, Guid clientRefId, int workspaceId, string command, string payload, TimeSpan expiresIn, CommandSender? sentBy, CancellationToken ct)
    {
        var request = new DeviceCommand(commandId, clientRefId, workspaceId, command, payload, clock.GetUtcNow().UtcDateTime + expiresIn);
        var result = await sender.SendAsync(request, e => Attribute(e, sentBy), ct);
        for (int i = 0; result.Outcome == DeviceCommandOutcome.Offline && i < _offlineRechecks.Count; i++)
        {
            await Task.Delay(_offlineRechecks[i], clock, ct);
            result = await sender.SendAsync(request, e => Attribute(e, sentBy), ct);
        }

        string? reason = result.Outcome switch
        {
            DeviceCommandOutcome.Sent => null,
            DeviceCommandOutcome.UnknownClient => UnknownClientReason,
            // The validator refuses these at publish time; this catches a definition saved before it did.
            DeviceCommandOutcome.Invalid => result.Violation!.Message,
            DeviceCommandOutcome.Offline => ClientCommandFailedEvent.NotConnectedReason,
            DeviceCommandOutcome.BrokerUnavailable => throw new InvalidOperationException("The event bus is unavailable; the command was not sent."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
        };

        if (reason is not null)
        {
            await eventBus.PublishAsync(new ClientCommandFailedEvent(clientRefId, result.ClientId, workspaceId, command, reason, commandId), ct);
        }
    }

    private static void Attribute(ClientCommandEvent @event, CommandSender? sentBy)
    {
        if (sentBy is not null)
        {
            @event.ActorSource = EventSource.Workflow;
            @event.ActorWorkflowRefId = sentBy.WorkflowRefId;
            @event.ActorRunRefId = sentBy.RunRefId;
        }
    }
}
