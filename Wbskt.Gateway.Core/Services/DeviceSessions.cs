using System.Security.Claims;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core.Services;

/// <summary>
/// A device session from admission to its end, the same over every transport. A gateway host asks
/// <see cref="AdmitAsync"/> before it accepts a connection, then hands the accepted connection to
/// <see cref="RunAsync"/> with two callbacks: its receive loop, and its own close. Everything between
/// (registering the device, announcing it, budgeting and routing its messages, and the end's reason,
/// metric and event) happens here, in one order, whatever the transport.
/// </summary>
public sealed class DeviceSessions
{
    private readonly IConnectionManager _connections;
    private readonly IClientAccessGate _accessGate;
    private readonly IEventBus _eventBus;
    private readonly GatewayMetrics _metrics;
    private readonly InboundMessageDispatcher _dispatcher;
    private readonly MessageBudgetOptions _budgetOptions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DeviceSessions> _logger;
    private readonly ILogger<DeviceInbox> _inboxLogger;
    private readonly string _hostId;

    public DeviceSessions(
        IConnectionManager connections,
        IClientAccessGate accessGate,
        IEventBus eventBus,
        BusInstanceId busInstanceId,
        GatewayMetrics metrics,
        InboundMessageDispatcher dispatcher,
        IOptions<MessageBudgetOptions> budgetOptions,
        IHostApplicationLifetime lifetime,
        ILogger<DeviceSessions> logger,
        ILogger<DeviceInbox> inboxLogger)
    {
        _connections = connections;
        _accessGate = accessGate;
        _eventBus = eventBus;
        _metrics = metrics;
        _dispatcher = dispatcher;
        _budgetOptions = budgetOptions.Value;
        _lifetime = lifetime;
        _logger = logger;
        _inboxLogger = inboxLogger;
        _hostId = busInstanceId.Value;
    }

    /// <summary>
    /// Decides whether the authenticated <paramref name="user"/> may open a session. The transport has
    /// already validated the token's signature, issuer and audience; this reads the device's identity
    /// from it and closes the window in which a revoked device's token still validates.
    /// </summary>
    public async Task<DeviceAdmission> AdmitAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryRead(user, out var identity))
        {
            return DeviceAdmission.Refuse(AdmissionRefusal.TokenInvalid);
        }

        if (await _accessGate.IsRefusedAsync(identity.ClientRefId, identity.IssuedAtUtc, cancellationToken))
        {
            _logger.LogWarning("Refusing a connection for revoked client {ClientRefId}.", identity.ClientRefId);
            return DeviceAdmission.Refuse(AdmissionRefusal.Revoked);
        }

        return DeviceAdmission.Admit(identity);
    }

    /// <summary>
    /// Runs one admitted device's session over <paramref name="connection"/> until it ends.
    /// </summary>
    /// <param name="identity">The device, as <see cref="AdmitAsync"/> admitted it.</param>
    /// <param name="connection">The transport's connection, accepted after admission.</param>
    /// <param name="receive">
    /// The transport's receive loop: reads until the connection ends, hands each text message to the
    /// inbox, and returns why it stopped (or the reason the inbox returned). It handles its own
    /// transport's errors; a cancellation or any other exception that escapes is classified here.
    /// </param>
    /// <param name="closeTransport">
    /// Closes the transport for the session's final reason, if it is still open. Called once, after the
    /// device is released and before its end is announced. It should not throw.
    /// </param>
    public async Task RunAsync(
        ClientIdentity identity,
        IDeviceConnection connection,
        Func<DeviceInbox, Task<DisconnectReason>> receive,
        Func<DisconnectReason, Task> closeTransport)
    {
        var clientRefId = identity.ClientRefId;

        // The newest connection wins: one this instance still holds for the device is closed as superseded.
        _connections.Register(clientRefId, connection);
        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);
        _metrics.Connected();
        var reason = DisconnectReason.ClientClosed;

        try
        {
            // Not tied to the connection: a device that drops straight away must still be recorded as
            // having connected, or its disconnect has nothing to pair with.
            await _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, _hostId), CancellationToken.None);

            var inbox = new DeviceInbox(new InboundContext(clientRefId, connection), new MessageBudget(_budgetOptions), _dispatcher, _metrics, _inboxLogger);
            reason = await receive(inbox);
        }
        catch (OperationCanceledException)
        {
            reason = _lifetime.ApplicationStopping.IsCancellationRequested ? DisconnectReason.ServerStopping : DisconnectReason.Aborted;
        }
        catch (Exception ex)
        {
            reason = DisconnectReason.Error;
            _logger.LogError(ex, "Unexpected error in the receive loop for client {ClientRefId}.", clientRefId);
        }
        finally
        {
            // A close someone asked for is the real reason, whatever the loop saw as it ended.
            reason = connection.CloseRequest?.Reason ?? reason;
            _connections.Unregister(clientRefId, connection);
            await CloseTransportAsync(clientRefId, closeTransport, reason);
            await AnnounceEndedAsync(clientRefId, connection, reason);
        }
    }

    private async Task CloseTransportAsync(Guid clientRefId, Func<DisconnectReason, Task> closeTransport, DisconnectReason reason)
    {
        try
        {
            await closeTransport(reason);
        }
        catch (Exception ex)
        {
            // The device is already released; its end must still be recorded and announced.
            _logger.LogWarning(ex, "Closing the transport for client {ClientRefId} failed.", clientRefId);
        }
    }

    private async Task AnnounceEndedAsync(Guid clientRefId, IDeviceConnection connection, DisconnectReason reason)
    {
        _metrics.Disconnected(reason);
        _logger.LogInformation("Client {ClientRefId} disconnected ({Reason}).", clientRefId, reason.Tag());

        // A superseded connection is not the device going offline: its newer connection, here or on
        // another instance, already announced it as connected.
        if (reason != DisconnectReason.Superseded)
        {
            var description = connection.CloseRequest?.Description ?? reason.Describe();
            await _eventBus.PublishAsync(new ClientDisconnectedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, description, _hostId), CancellationToken.None);
        }
    }
}
