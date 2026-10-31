using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Infrastructure.Devices;

/// <summary>How commands wait for an offline device, from <c>CommandQueue</c>.</summary>
public sealed class CommandQueueOptions
{
    public const string SectionName = "CommandQueue";

    /// <summary>
    /// The furthest ahead a command's expiry may be, waiting or not. A command is never acted on later
    /// than this after it was sent, whatever the configuration says.
    /// </summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    /// <summary>How many commands may wait for one device; a send past it is refused.</summary>
    public int MaxPerClient { get; set; } = 100;

    /// <summary>The expiry of a command sent without one, in minutes. Capped at <see cref="MaxLifetime"/>.</summary>
    public int DefaultExpiryMinutes { get; set; } = (int)MaxLifetime.TotalMinutes;

    public TimeSpan DefaultExpiry => TimeSpan.FromMinutes(Math.Clamp(DefaultExpiryMinutes, 1, (int)MaxLifetime.TotalMinutes));
}

/// <summary>
/// Puts commands in an offline device's queue and sends them on when it connects. Used by the sender
/// for a device it finds offline, and by the management host when a device connects, when a socket
/// host hands back a command whose connection had gone, and when the queue is swept.
/// </summary>
public sealed class DeviceCommandQueue(
    IQueuedCommandStore store,
    IClientCommandTargetProvider targets,
    IEventBus eventBus,
    IOptions<CommandQueueOptions> options,
    TimeProvider clock,
    ILogger<DeviceCommandQueue> logger)
{
    public CommandQueueOptions Options => options.Value;

    /// <summary>The expiry a command waits under: the one it was sent with, or the default from now.</summary>
    public DateTime ExpiryFor(DateTime? requestedUtc) => requestedUtc ?? clock.GetUtcNow().UtcDateTime + Options.DefaultExpiry;

    /// <summary>
    /// Queues the command and announces it with <paramref name="announcement"/>, which names who sent
    /// it. False, with nothing queued or announced, when the device already has
    /// <paramref name="maxPerClient"/> commands waiting.
    /// </summary>
    public async Task<bool> QueueAsync(QueuedCommand command, ClientCommandQueuedEvent announcement, int maxPerClient, CancellationToken ct)
    {
        if (!await store.EnqueueAsync(command, maxPerClient, ct))
        {
            return false;
        }

        // The command waits whether or not this is heard; a broker that is down only costs the row in
        // the log and the console's live update.
        try
        {
            await eventBus.PublishAsync(announcement, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Command '{CommandId}' is queued for client '{ClientRefId}', but the queueing could not be announced. {Message}",
                command.CommandId, command.ClientRefId, ex.Message);
        }

        // The device may have connected between the sender reading it offline and the row going in, after
        // the connect had already delivered an empty queue. Looking again after the insert closes that:
        // either the connect's delivery sees the row, or this read sees the connection.
        var target = await targets.FindAsync(command.ClientRefId, ct);
        if (target is { IsConnected: true, HostId: { Length: > 0 } hostId })
        {
            await DeliverAsync(command.ClientRefId, target.ClientId, command.WorkspaceId, hostId, ct);
        }

        return true;
    }

    /// <summary>
    /// Sends the device's waiting commands, oldest first, addressed to the socket host now holding it.
    /// Returns how many went out. A broker failure puts the unsent ones back and is rethrown.
    /// </summary>
    public async Task<int> DeliverAsync(Guid clientRefId, int clientId, int workspaceId, string hostId, CancellationToken ct)
    {
        var taken = await store.TakeAsync(clientId, clock.GetUtcNow().UtcDateTime, ct);
        for (int i = 0; i < taken.Count; i++)
        {
            var command = taken[i];
            var @event = new ClientCommandEvent(clientRefId, clientId, workspaceId, command.Type, command.Payload, command.CommandId,
                TargetHostId: hostId, ExpiresAtUtc: command.ExpiresAtUtc);
            command.Attribute(@event);

            try
            {
                await eventBus.PublishAsync(@event, ct);
            }
            catch (Exception ex)
            {
                // The rows are already out of the table; the rest go back so the next connect, or a retry
                // of this one, sends them. Beyond the limit: they were accepted once already.
                foreach (var unsent in taken.Skip(i))
                {
                    await store.EnqueueAsync(unsent, int.MaxValue, CancellationToken.None);
                }

                logger.LogWarning("Delivering queued commands to client '{ClientRefId}' stopped after {Sent} of {Count}: {Message}", clientRefId, i, taken.Count, ex.Message);
                throw;
            }
        }

        if (taken.Count > 0)
        {
            logger.LogInformation("Delivered {Count} queued commands to client '{ClientRefId}' on host '{HostId}'.", taken.Count, clientRefId, hostId);
        }

        return taken.Count;
    }

    /// <summary>
    /// Ends every command the device has waiting with a <see cref="ClientCommandFailedEvent"/> giving
    /// <paramref name="reason"/>. Returns how many there were.
    /// </summary>
    public async Task<int> DropAsync(int clientId, string reason, CancellationToken ct)
    {
        var dropped = await store.DropAsync(clientId, ct);
        await AnnounceFailedAsync(dropped, reason, ct);
        return dropped.Count;
    }

    /// <summary>Ends up to <paramref name="take"/> commands whose expiry passed while they waited. Returns how many.</summary>
    public async Task<int> ExpireAsync(int take, CancellationToken ct)
    {
        var expired = await store.TakeExpiredAsync(clock.GetUtcNow().UtcDateTime, take, ct);
        await AnnounceFailedAsync(expired, ClientCommandFailedEvent.ExpiredWhileQueuedReason, ct);
        return expired.Count;
    }

    // The rows are gone whatever happens here, so one failed announcement does not stop the rest.
    private async Task AnnounceFailedAsync(IReadOnlyList<QueuedCommand> commands, string reason, CancellationToken ct)
    {
        foreach (var command in commands)
        {
            try
            {
                await eventBus.PublishAsync(new ClientCommandFailedEvent(command.ClientRefId, command.ClientId, command.WorkspaceId, command.Type, reason, command.CommandId), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Queued command '{CommandId}' for client '{ClientRefId}' ended ({Reason}), but that could not be announced. {Message}",
                    command.CommandId, command.ClientRefId, reason, ex.Message);
            }
        }
    }
}
