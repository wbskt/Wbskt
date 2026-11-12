using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Handlers;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class ConnectionSupersededHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);

    [Fact]
    public async Task A_newer_connect_on_another_host_closes_ours_as_superseded()
    {
        var ours = Hold(DateTime.UtcNow.AddMinutes(-5));

        await Handler().Consume(Context(new ClientConnectedEvent(ClientRefId, 1, 2, "other-host") { CreatedAtUtc = DateTime.UtcNow }));

        Assert.Equal(DisconnectReason.Superseded, ours.CloseRequest?.Reason);
    }

    [Fact]
    public async Task A_connection_another_host_had_restored_does_not_close_ours()
    {
        var ours = Hold(DateTime.UtcNow.AddMinutes(-5));

        await Handler().Consume(Context(new ClientConnectedEvent(ClientRefId, 1, 2, "other-host", IsRestored: true) { CreatedAtUtc = DateTime.UtcNow }));

        Assert.Null(ours.CloseRequest);
    }

    private FakeClientConnection Hold(DateTime establishedAt)
    {
        var connection = new FakeClientConnection { ClientId = 1, WorkspaceId = 2, EstablishedAtUtc = establishedAt, TokenIssuedAtUtc = establishedAt };
        _connections.Register(ClientRefId, connection);
        return connection;
    }

    private ConnectionSupersededHandler Handler() => new(_connections, new BusInstanceId("this-host"), NullLogger<ConnectionSupersededHandler>.Instance);

    private static ConsumeContext<ClientConnectedEvent> Context(ClientConnectedEvent message)
    {
        var context = new Mock<ConsumeContext<ClientConnectedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        return context.Object;
    }
}
