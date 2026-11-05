using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Commands and pings to a client. Unlike the services' post-commit events, the publish here is the
/// action itself, so it goes out on the real bus and a broker that fails or does not answer in time is
/// an <see cref="ErrorType.Unavailable"/> result (503, retry) rather than a queued send reported as
/// done.
/// </summary>
public interface IClientCommandService
{
    /// <summary>
    /// Validates the command, checks the client is this workspace's, and publishes it to the connected
    /// client or queues it for an offline one (status <c>queued</c>, delivered when it connects).
    /// <c>COMMAND_QUEUE_FULL</c> (409) when an offline client already has the most commands waiting,
    /// <c>CLIENT_INACTIVE</c> (409) for an offline client that is not registered, and
    /// <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.
    /// </summary>
    Task<Result<ClientCommandResponse>> SendAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <c>sys.ota.update</c> to a client that lists <c>ota</c> in its capabilities, through the
    /// same path as <see cref="SendAsync"/>, so an offline client gets it queued. It always carries an
    /// expiry. <c>OTA_URL_INVALID</c>, <c>OTA_SHA256_INVALID</c>, <c>OTA_VERSION_INVALID</c> and
    /// <c>OTA_SIZE_INVALID</c> (400) for a bad request, <c>OTA_NOT_SUPPORTED</c> (409) for a client
    /// that does not take updates, and the errors of <see cref="SendAsync"/>.
    /// </summary>
    Task<Result<ClientCommandResponse>> SendOtaAsync(int workspaceId, Guid clientRefId, ClientOtaRequest request, CancellationToken cancellationToken = default);

    /// <summary>The commands waiting for a client of this workspace, oldest first.</summary>
    Task<Result<IReadOnlyList<QueuedClientCommandResponse>>> GetQueuedAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a command still waiting for the client. <c>COMMAND_NOT_QUEUED</c> (404) when it is not
    /// waiting: delivered, expired, cancelled already, or not this client's.
    /// </summary>
    Task<Result> CancelAsync(int workspaceId, Guid clientRefId, Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>Pings a client of this workspace; <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.</summary>
    Task<Result> PingAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
}
