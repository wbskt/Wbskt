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
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>
/// A client session as every transport gets it from Gateway.Core: who is admitted, and, around the
/// transport's own receive loop and close, what the client sees (its close code) and what the rest of
/// the system hears (connected, disconnected).
/// </summary>
public sealed class ClientSessionTests
{
    private const string ThisHost = "gateway-a";
    private const string Message = """{"type":"t","payload":{}}""";
    private static readonly Guid ClientRefId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly ClientAdmission Admitted = ClientAdmission.Admit(new ClientIdentity(ClientRefId, 42, 7, DateTime.UtcNow));

    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IClientAccessGate> _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);

    private readonly SupersedeMonitor _supersedes = new();

    private ClientSessions Sessions(MessageBudgetOptions? budget = null)
    {
        var metrics = new GatewayMetrics(GatewayTransport.WebSocket);
        var dispatcher = new InboundMessageDispatcher([], new ClientMessageHandler(_bus.Object), metrics, NullLogger<InboundMessageDispatcher>.Instance);
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(x => x.ApplicationStopping).Returns(_stopping.Token);
        return new ClientSessions(_connections, _gate.Object, _bus.Object, new BusInstanceId(ThisHost), metrics, dispatcher,
            Options.Create(budget ?? new MessageBudgetOptions()), lifetime.Object, NullLogger<ClientSessions>.Instance, NullLogger<ClientInbox>.Instance, _supersedes);
    }

    private static FakeClientConnection Connection() => new() { ClientId = 42, WorkspaceId = 7, TokenIssuedAtUtc = DateTime.UtcNow };

    private static ClaimsPrincipal ClientUser() => new(new ClaimsIdentity(ClientClaims.For(ClientRefId, "sensor", Guid.NewGuid(), 42, 7), "test"));

    private void VerifyDisconnected(string reason) =>
        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == ClientRefId && e.Reason == reason && e.HostId == ThisHost), CancellationToken.None), Times.Once);

    [Fact]
    public async Task A_well_formed_token_that_is_not_revoked_is_admitted()
    {
        var admission = await Sessions().AdmitAsync(ClientUser(), CancellationToken.None);

        Assert.True(admission.IsAdmitted);
        Assert.Equal(ClientRefId, admission.Identity.ClientRefId);
        Assert.Equal(42, admission.Identity.ClientId);
        Assert.Equal(7, admission.Identity.WorkspaceId);
    }

    [Fact]
    public async Task A_token_missing_a_client_claim_is_refused_as_invalid()
    {
        var admission = await Sessions().AdmitAsync(new ClaimsPrincipal(new ClaimsIdentity("test")), CancellationToken.None);

        Assert.False(admission.IsAdmitted);
        Assert.Equal(AdmissionRefusal.TokenInvalid, admission.Refusal);
        _gate.Verify(x => x.IsRefusedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_revoked_client_is_refused()
    {
        _gate.Setup(x => x.IsRefusedAsync(ClientRefId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var admission = await Sessions().AdmitAsync(ClientUser(), CancellationToken.None);

        Assert.False(admission.IsAdmitted);
        Assert.Equal(AdmissionRefusal.Revoked, admission.Refusal);
    }

    [Fact]
    public async Task A_session_announces_the_client_and_releases_it_before_the_transport_closes()
    {
        var connection = Connection();
        var registeredDuringLoop = false;
        var registeredAtClose = true;
        connection.OnTransportClose = _ => registeredAtClose = _connections.GetConnection(ClientRefId) != null;

        await Sessions().RunAsync(Admitted, connection,
            _ =>
            {
                registeredDuringLoop = ReferenceEquals(connection, _connections.GetConnection(ClientRefId));
                return Task.FromResult(DisconnectReason.Aborted);
            });

        Assert.True(registeredDuringLoop);
        Assert.False(registeredAtClose);
        Assert.Equal(DisconnectReason.Aborted, Assert.Single(connection.TransportCloses));
        _bus.Verify(x => x.PublishAsync(It.Is<ClientConnectedEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == 42 && e.WorkspaceId == 7 && e.HostId == ThisHost), It.IsAny<CancellationToken>()), Times.Once);
        VerifyDisconnected(DisconnectReason.Aborted.Describe());
    }

    [Fact]
    public async Task A_broker_outage_drops_the_clients_messages_but_not_its_session()
    {
        var connectAttempts = 0;
        _bus.Setup(x => x.PublishAsync(It.IsAny<ClientConnectedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => connectAttempts++)
            .Returns(() => connectAttempts == 1 ? Task.FromException(new InvalidOperationException("broker down")) : Task.CompletedTask);
        _bus.Setup(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        var connection = Connection();
        DisconnectReason? result = DisconnectReason.Error;

        await Sessions().RunAsync(Admitted, connection,
            async inbox =>
            {
                result = await inbox.ReceiveAsync(Message, CancellationToken.None);
                // The connect announcement is retried a second later, while the session lasts.
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                return DisconnectReason.ClientClosed;
            });

        Assert.Null(result);
        Assert.Equal(2, connectAttempts);
        VerifyDisconnected(DisconnectReason.ClientClosed.Describe());
    }

    [Fact]
    public async Task A_requested_close_is_the_reason_whatever_the_loop_saw()
    {
        var connection = Connection();

        await Sessions().RunAsync(Admitted, connection,
            async _ =>
            {
                await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");
                return DisconnectReason.ClientClosed;
            });

        Assert.Equal(DisconnectReason.Revoked, Assert.Single(connection.TransportCloses));
        VerifyDisconnected("Client deleted.");
    }

    [Fact]
    public async Task A_superseded_session_does_not_announce_the_client_offline()
    {
        var old = Connection();
        var fresh = Connection();

        await Sessions().RunAsync(Admitted, old,
            _ =>
            {
                _connections.Register(ClientRefId, fresh);
                return Task.FromResult(DisconnectReason.ClientClosed);
            });

        Assert.Equal(new CloseRequest(DisconnectReason.Superseded, ConnectionManager.SupersededDescription), Assert.Single(old.Closes));
        Assert.Same(fresh, _connections.GetConnection(ClientRefId));
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_superseded_again_and_again_is_reported_as_sharing_credentials()
    {
        for (var i = 0; i < SupersedeMonitor.Threshold; i++)
        {
            var old = Connection();
            await Sessions().RunAsync(Admitted, old,
                _ =>
                {
                    _connections.Register(ClientRefId, Connection());
                    return Task.FromResult(DisconnectReason.ClientClosed);
                });
        }

        _bus.Verify(x => x.PublishAsync(
            It.Is<ClientCredentialsSharedEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == 42 && e.WorkspaceId == 7 && e.Supersedes == SupersedeMonitor.Threshold),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_loop_cancelled_by_shutdown_ends_as_server_stopping()
    {
        var connection = Connection();
        await _stopping.CancelAsync();

        await Sessions().RunAsync(Admitted, connection, _ => throw new OperationCanceledException());

        Assert.Equal(DisconnectReason.ServerStopping, Assert.Single(connection.TransportCloses));
        VerifyDisconnected(DisconnectReason.ServerStopping.Describe());
    }

    [Fact]
    public async Task A_client_closing_to_update_as_the_host_stops_is_still_announced_as_updating()
    {
        var connection = Connection();

        await Sessions().RunAsync(Admitted, connection, async _ =>
        {
            await connection.RequestCloseAsync(DisconnectReason.ServerStopping, DisconnectReason.ServerStopping.Describe());
            return DisconnectReason.Updating;
        });

        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.IsUpdating), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_loop_cancelled_otherwise_ends_as_aborted()
    {
        var connection = Connection();

        await Sessions().RunAsync(Admitted, connection, _ => throw new OperationCanceledException());

        VerifyDisconnected(DisconnectReason.Aborted.Describe());
    }

    [Fact]
    public async Task A_loop_that_throws_still_releases_and_announces_the_client()
    {
        var connection = Connection();

        await Sessions().RunAsync(Admitted, connection, _ => throw new InvalidOperationException("boom"));

        Assert.Null(_connections.GetConnection(ClientRefId));
        VerifyDisconnected(DisconnectReason.Error.Describe());
    }

    [Theory]
    [InlineData(DisconnectReason.Updating, true)]
    [InlineData(DisconnectReason.ClientClosed, false)]
    public async Task Only_a_client_closing_to_update_is_announced_as_updating(DisconnectReason reason, bool updating)
    {
        var connection = Connection();

        await Sessions().RunAsync(Admitted, connection, _ => Task.FromResult(reason));

        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.IsUpdating == updating), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_transport_close_that_throws_does_not_stop_the_end_being_announced()
    {
        var connection = Connection();
        connection.OnTransportClose = _ => throw new InvalidOperationException("boom");

        await Sessions().RunAsync(Admitted, connection, _ => Task.FromResult(DisconnectReason.ClientClosed));

        VerifyDisconnected(DisconnectReason.ClientClosed.Describe());
    }

    [Fact]
    public async Task A_client_revoked_after_admission_is_closed_once_registered()
    {
        var connection = Connection();
        _gate.Setup(x => x.IsRefusedHere(ClientRefId, It.IsAny<DateTime>())).Returns(true);
        CloseRequest? seenByLoop = null;

        await Sessions().RunAsync(Admitted, connection,
            _ =>
            {
                seenByLoop = connection.CloseRequest;
                return Task.FromResult(DisconnectReason.ClientClosed);
            });

        Assert.Equal(DisconnectReason.Revoked, seenByLoop?.Reason);
        VerifyDisconnected(DisconnectReason.Revoked.Describe());
    }

    [Fact]
    public async Task A_disconnect_the_bus_cannot_take_does_not_escape_the_session()
    {
        var connection = Connection();
        _bus.Setup(x => x.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        await Sessions().RunAsync(Admitted, connection, _ => Task.FromResult(DisconnectReason.ClientClosed));

        Assert.Null(_connections.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_refused_admission_cannot_run_a_session()
    {
        var connection = Connection();
        var refused = ClientAdmission.Refuse(AdmissionRefusal.Revoked);

        await Assert.ThrowsAsync<ArgumentException>(() => Sessions().RunAsync(refused, connection, _ => Task.FromResult(DisconnectReason.ClientClosed)));

        Assert.Null(_connections.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_client_that_stays_over_its_budget_is_closed_as_throttled()
    {
        var connection = Connection();
        var results = new List<DisconnectReason?>();

        await Sessions(new MessageBudgetOptions { PerSecond = 0.1, Burst = 1, CloseAfterDropped = 2 }).RunAsync(Admitted, connection,
            async inbox =>
            {
                for (var i = 0; i < 3; i++)
                {
                    results.Add(await inbox.ReceiveAsync(Message, CancellationToken.None));
                }

                return DisconnectReason.ClientClosed;
            });

        Assert.Equal([null, null, DisconnectReason.Throttled], results);
        Assert.Equal(new CloseRequest(DisconnectReason.Throttled, "Too many messages."), Assert.Single(connection.Closes));
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyDisconnected("Too many messages.");
    }

    [Fact]
    public async Task A_message_over_the_size_limit_is_closed_with_1009_whatever_the_transport_let_through()
    {
        var connection = Connection();
        var tooBig = $$"""{"type":"t","payload":"{{new string('é', ClientProtocol.MaxMessageBytes / 2)}}"}""";
        DisconnectReason? result = null;

        await Sessions().RunAsync(Admitted, connection,
            async inbox =>
            {
                result = await inbox.ReceiveAsync(tooBig, CancellationToken.None);
                return DisconnectReason.ClientClosed;
            });

        Assert.Equal(DisconnectReason.MessageTooBig, result);
        Assert.Equal(new CloseRequest(DisconnectReason.MessageTooBig, ClientInbox.TooBigDescription), Assert.Single(connection.Closes));
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Messages_after_a_requested_close_are_ignored()
    {
        var connection = Connection();
        DisconnectReason? result = DisconnectReason.Error;

        await Sessions().RunAsync(Admitted, connection,
            async inbox =>
            {
                await connection.RequestCloseAsync(DisconnectReason.Revoked, "x");
                result = await inbox.ReceiveAsync(Message, CancellationToken.None);
                return DisconnectReason.ClientClosed;
            });

        Assert.Null(result);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
