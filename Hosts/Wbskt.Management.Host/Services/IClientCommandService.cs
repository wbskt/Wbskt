using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

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
    /// Validates the command, checks the device is this workspace's and online, and publishes it.
    /// <c>DEVICE_OFFLINE</c> (409) when the device is not connected, since commands are delivered live
    /// or not at all; <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.
    /// </summary>
    Task<Result<ClientCommandResponse>> SendAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pings a device of this workspace; <c>EVENT_BUS_UNAVAILABLE</c> (503) when the broker is down.</summary>
    Task<Result> PingAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
}
