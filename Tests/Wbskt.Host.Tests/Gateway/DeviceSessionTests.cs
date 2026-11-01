using System.Net.WebSockets;
using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Gateway.WebSocket.Host.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>
/// A device session as every transport gets it from Gateway.Core: who is admitted, and, around the
/// transport's own receive loop and close, what the device sees (its close code) and what the rest of
/// the system hears (connected, disconnected).
/// </summary>
public sealed class DeviceSessionTests
{
    private const string ThisHost = "gateway-a";
    private const string Message = """{"type":"t","payload":{}}""";
    private static readonly Guid ClientRefId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DeviceAdmission Admitted = DeviceAdmission.Admit(new ClientIdentity(ClientRefId, 42, 7, DateTime.UtcNow));

    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IClientAccessGate> _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);

    private DeviceSessions Sessions(MessageBudgetOptions? budget = null)
    {
        var metrics = new GatewayMetrics(GatewayTransport.WebSocket);
        var dispatcher = new InboundMessageDispatcher([], new DeviceMessageHandler(_bus.Object), metrics, NullLogger<InboundMessageDispatcher>.Instance);
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(x => x.ApplicationStopping).Returns(_stopping.Token);
        return new DeviceSessions(_connections, _gate.Object, _bus.Object, new BusInstanceId(ThisHost), metrics, dispatcher,
            Options.Create(budget ?? new MessageBudgetOptions()), lifetime.Object, NullLogger<DeviceSessions>.Instance, NullLogger<DeviceInbox>.Instance);
    }

    private static (ClientConnection Connection, Mock<WebSocket> Socket) Connection()
    {
        var socket = new Mock<WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 42, WorkspaceId = 7 }, socket);
    }

    private static ClaimsPrincipal DeviceUser() => new(new ClaimsIdentity(ClientClaims.For(ClientRefId, "sensor", Guid.NewGuid(), 42, 7), "test"));

    private void VerifyDisconnected(string reason) =>
        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == ClientRefId && e.Reason == reason && e.HostId == ThisHost), CancellationToken.None), Times.Once);

    [Fact]
    public async Task A_well_formed_token_that_is_not_revoked_is_admitted()
    {
        var admission = await Sessions().AdmitAsync(DeviceUser(), CancellationToken.None);

        Assert.True(admission.IsAdmitted);
        Assert.Equal(ClientRefId, admission.Identity.ClientRefId);
        Assert.Equal(42, admission.Identity.ClientId);
        Assert.Equal(7, admission.Identity.WorkspaceId);
    }

    [Fact]
    public async Task A_token_missing_a_device_claim_is_refused_as_invalid()
    {
        var admission = await Sessions().AdmitAsync(new ClaimsPrincipal(new ClaimsIdentity("test")), CancellationToken.None);

        Assert.False(admission.IsAdmitted);
        Assert.Equal(AdmissionRefusal.TokenInvalid, admission.Refusal);
        _gate.Verify(x => x.IsRefusedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_revoked_device_is_refused()
    {
        _gate.Setup(x => x.IsRefusedAsync(ClientRefId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var admission = await Sessions().AdmitAsync(DeviceUser(), CancellationToken.None);

        Assert.False(admission.IsAdmitted);
        Assert.Equal(AdmissionRefusal.Revoked, admission.Refusal);
    }

    [Fact]
    public async Task A_session_announces_the_device_and_releases_it_before_the_transport_closes()
    {
        var (connection, _) = Connection();
        var registeredDuringLoop = false;
        var registeredAtClose = true;
        DisconnectReason? closedWith = null;

        await Sessions().RunAsync(Admitted, connection,
            _ =>
            {
                registeredDuringLoop = ReferenceEquals(connection, _connections.GetConnection(ClientRefId));
                return Task.FromResult(DisconnectReason.Aborted);
            },
            reason =>
            {
                registeredAtClose = _connections.GetConnection(ClientRefId) != null;
                closedWith = reason;
                return Task.CompletedTask;
            });

        Assert.True(registeredDuringLoop);
        Assert.False(registeredAtClose);
        Assert.Equal(DisconnectReason.Aborted, closedWith);
        _bus.Verify(x => x.PublishAsync(It.Is<ClientConnectedEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == 42 && e.WorkspaceId == 7 && e.HostId == ThisHost), CancellationToken.None), Times.Once);
        VerifyDisconnected(DisconnectReason.Aborted.Describe());
    }

    [Fact]
    public async Task A_requested_close_is_the_reason_whatever_the_loop_saw()
    {
        var (connection, _) = Connection();
        DisconnectReason? closedWith = null;

        await Sessions().RunAsync(Admitted, connection,
            async _ =>
            {
                await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");
                return DisconnectReason.ClientClosed;
            },
            reason =>
            {
                closedWith = reason;
                return Task.CompletedTask;
            });

        Assert.Equal(DisconnectReason.Revoked, closedWith);
        VerifyDisconnected("Client deleted.");
    }

    [Fact]
    public async Task A_superseded_session_does_not_announce_the_device_offline()
    {
        var (old, oldSocket) = Connection();
        var (fresh, _) = Connection();

        await Sessions().RunAsync(Admitted, old,
            _ =>
            {
                _connections.Register(ClientRefId, fresh);
                return Task.FromResult(DisconnectReason.ClientClosed);
            },
            _ => Task.CompletedTask);

        oldSocket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, ConnectionManager.SupersededDescription, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(fresh, _connections.GetConnection(ClientRefId));
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_loop_cancelled_by_shutdown_ends_as_server_stopping()
    {
        var (connection, _) = Connection();
        DisconnectReason? closedWith = null;
        await _stopping.CancelAsync();

        await Sessions().RunAsync(Admitted, connection,
            _ => throw new OperationCanceledException(),
            reason =>
            {
                closedWith = reason;
                return Task.CompletedTask;
            });

        Assert.Equal(DisconnectReason.ServerStopping, closedWith);
        VerifyDisconnected(DisconnectReason.ServerStopping.Describe());
    }

    [Fact]
    public async Task A_loop_cancelled_otherwise_ends_as_aborted()
    {
        var (connection, _) = Connection();

        await Sessions().RunAsync(Admitted, connection, _ => throw new OperationCanceledException(), _ => Task.CompletedTask);

        VerifyDisconnected(DisconnectReason.Aborted.Describe());
    }

    [Fact]
    public async Task A_loop_that_throws_still_releases_and_announces_the_device()
    {
        var (connection, _) = Connection();

        await Sessions().RunAsync(Admitted, connection, _ => throw new InvalidOperationException("boom"), _ => Task.CompletedTask);

        Assert.Null(_connections.GetConnection(ClientRefId));
        VerifyDisconnected(DisconnectReason.Error.Describe());
    }

    [Fact]
    public async Task A_transport_close_that_throws_does_not_stop_the_end_being_announced()
    {
        var (connection, _) = Connection();

        await Sessions().RunAsync(Admitted, connection,
            _ => Task.FromResult(DisconnectReason.ClientClosed),
            _ => throw new InvalidOperationException("boom"));

        VerifyDisconnected(DisconnectReason.ClientClosed.Describe());
    }

    [Fact]
    public async Task A_device_revoked_after_admission_is_closed_once_registered()
    {
        var (connection, _) = Connection();
        _gate.Setup(x => x.IsRefusedHere(ClientRefId, It.IsAny<DateTime>())).Returns(true);
        CloseRequest? seenByLoop = null;

        await Sessions().RunAsync(Admitted, connection,
            _ =>
            {
                seenByLoop = connection.CloseRequest;
                return Task.FromResult(DisconnectReason.ClientClosed);
            },
            _ => Task.CompletedTask);

        Assert.Equal(DisconnectReason.Revoked, seenByLoop?.Reason);
        VerifyDisconnected(DisconnectReason.Revoked.Describe());
    }

    [Fact]
    public async Task A_disconnect_the_bus_cannot_take_does_not_escape_the_session()
    {
        var (connection, _) = Connection();
        _bus.Setup(x => x.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        await Sessions().RunAsync(Admitted, connection, _ => Task.FromResult(DisconnectReason.ClientClosed), _ => Task.CompletedTask);

        Assert.Null(_connections.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_refused_admission_cannot_run_a_session()
    {
        var (connection, _) = Connection();
        var refused = DeviceAdmission.Refuse(AdmissionRefusal.Revoked);

        await Assert.ThrowsAsync<ArgumentException>(() => Sessions().RunAsync(refused, connection, _ => Task.FromResult(DisconnectReason.ClientClosed), _ => Task.CompletedTask));

        Assert.Null(_connections.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_device_that_stays_over_its_budget_is_closed_with_1008()
    {
        var (connection, socket) = Connection();
        var results = new List<DisconnectReason?>();

        await Sessions(new MessageBudgetOptions { PerSecond = 0.1, Burst = 1, CloseAfterDropped = 2 }).RunAsync(Admitted, connection,
            async inbox =>
            {
                for (var i = 0; i < 3; i++)
                {
                    results.Add(await inbox.ReceiveAsync(Message, CancellationToken.None));
                }

                return DisconnectReason.ClientClosed;
            },
            _ => Task.CompletedTask);

        Assert.Equal([null, null, DisconnectReason.Throttled], results);
        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Too many messages.", It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyDisconnected("Too many messages.");
    }

    [Fact]
    public async Task A_message_over_the_size_limit_is_closed_with_1009_whatever_the_transport_let_through()
    {
        var (connection, socket) = Connection();
        var tooBig = $$"""{"type":"t","payload":"{{new string('é', SocketProtocol.MaxMessageBytes / 2)}}"}""";
        DisconnectReason? result = null;

        await Sessions().RunAsync(Admitted, connection,
            async inbox =>
            {
                result = await inbox.ReceiveAsync(tooBig, CancellationToken.None);
                return DisconnectReason.ClientClosed;
            },
            _ => Task.CompletedTask);

        Assert.Equal(DisconnectReason.MessageTooBig, result);
        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, DeviceInbox.TooBigDescription, It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Messages_after_a_requested_close_are_ignored()
    {
        var (connection, _) = Connection();
        DisconnectReason? result = DisconnectReason.Error;

        await Sessions().RunAsync(Admitted, connection,
            async inbox =>
            {
                await connection.RequestCloseAsync(DisconnectReason.Revoked, "x");
                result = await inbox.ReceiveAsync(Message, CancellationToken.None);
                return DisconnectReason.ClientClosed;
            },
            _ => Task.CompletedTask);

        Assert.Null(result);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>The close codes are part of the device protocol: moving the reasons into Gateway.Core must not change them.</summary>
public sealed class WebSocketCloseStatusesTests
{
    [Theory]
    [InlineData(DisconnectReason.Revoked, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.Superseded, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.Throttled, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.MessageTooBig, WebSocketCloseStatus.MessageTooBig)]
    [InlineData(DisconnectReason.ServerStopping, WebSocketCloseStatus.EndpointUnavailable)]
    [InlineData(DisconnectReason.ClientClosed, WebSocketCloseStatus.NormalClosure)]
    public void Each_reason_keeps_its_close_code(DisconnectReason reason, WebSocketCloseStatus expected)
    {
        Assert.Equal(expected, WebSocketCloseStatuses.For(reason));
    }
}
