using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Commands and pings to a device. Unlike the services' post-commit events, the publish here is the
/// action itself, so it goes out on the real bus and a broker that fails or does not answer in time is
/// an <see cref="ErrorType.Unavailable"/> result (503, retry) rather than a queued send reported as
/// done.
/// </summary>
public interface IClientCommandService
{
    /// <summary>
    /// Validates the command, checks the device is this workspace's, and publishes it to the connected
    /// device or queues it for an offline one (status <c>queued</c>, delivered when it connects).
    /// <c>COMMAND_QUEUE_FULL</c> (409) when an offline device already has the most commands waiting,
    /// <c>DEVICE_INACTIVE</c> (409) for an offline device that is not registered, and
    /// <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.
    /// </summary>
    Task<Result<ClientCommandResponse>> SendAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken = default);

    /// <summary>The commands waiting for a device of this workspace, oldest first.</summary>
    Task<Result<IReadOnlyList<QueuedClientCommandResponse>>> GetQueuedAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a command still waiting for the device. <c>COMMAND_NOT_QUEUED</c> (404) when it is not
    /// waiting: delivered, expired, cancelled already, or not this device's.
    /// </summary>
    Task<Result> CancelAsync(int workspaceId, Guid clientRefId, Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>Pings a device of this workspace; <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.</summary>
    Task<Result> PingAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
}
