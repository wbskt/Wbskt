using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class ConnectionManagerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void A_reconnect_on_the_same_host_wins_and_the_old_connection_is_closed_as_superseded()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var old = Connection();
        var fresh = Connection();

        manager.Register(ClientRefId, old);
        manager.Register(ClientRefId, fresh);

        Assert.Same(fresh, manager.GetConnection(ClientRefId));
        Assert.Equal(DisconnectReason.Superseded, old.CloseRequest?.Reason);
        Assert.Equal(DisconnectReason.Superseded, Assert.Single(old.Closes).Reason);
        Assert.Null(fresh.CloseRequest);
    }

    [Fact]
    public void The_old_connections_cleanup_does_not_remove_its_replacement()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var old = Connection();
        var fresh = Connection();
        manager.Register(ClientRefId, old);
        manager.Register(ClientRefId, fresh);

        Assert.False(manager.Unregister(ClientRefId, old));
        Assert.Same(fresh, manager.GetConnection(ClientRefId));
        Assert.True(manager.Unregister(ClientRefId, fresh));
        Assert.Null(manager.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_close_is_sent_once_however_often_it_is_asked_for()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var connection = Connection();
        manager.Register(ClientRefId, connection);

        Assert.True(await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Revoked, "Client deleted."));
        await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Superseded, "again");

        Assert.Equal(new CloseRequest(DisconnectReason.Revoked, "Client deleted."), connection.CloseRequest);
        Assert.Equal(new CloseRequest(DisconnectReason.Revoked, "Client deleted."), Assert.Single(connection.Closes));
        // The connection stays registered: only its receive loop unregisters it.
        Assert.Same(connection, manager.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task Asking_a_host_without_the_client_to_close_it_is_a_no_op()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);

        Assert.False(await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Revoked, "x"));
    }

    private static FakeDeviceConnection Connection() => new() { ClientId = 1, WorkspaceId = 2 };
}
