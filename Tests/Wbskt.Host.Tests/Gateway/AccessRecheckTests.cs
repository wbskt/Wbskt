using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Gateway.Core.HostedServices;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

public sealed class AccessRecheckTests
{
    private static readonly DateTime IssuedAt = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);
    private readonly Mock<IClientAccessGate> _gate = new();

    [Fact]
    public async Task A_connection_whose_token_the_cutoffs_now_refuse_is_closed_as_revoked()
    {
        Guid revokedRef = Guid.NewGuid(), keptRef = Guid.NewGuid();
        var revoked = Hold(revokedRef);
        var kept = Hold(keptRef);
        _gate.Setup(g => g.IsRefusedAsync(revokedRef, IssuedAt, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Recheck().TickAsync(CancellationToken.None);

        Assert.Equal(DisconnectReason.Revoked, revoked.CloseRequest?.Reason);
        Assert.Null(kept.CloseRequest);
        _gate.Verify(g => g.IsRefusedAsync(keptRef, IssuedAt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_connection_already_closing_or_closed_is_not_asked_about()
    {
        var closing = Hold(Guid.NewGuid());
        await closing.RequestCloseAsync(DisconnectReason.Superseded, "newer");
        var closed = Hold(Guid.NewGuid());
        closed.Open = false;

        await Recheck().TickAsync(CancellationToken.None);

        _gate.Verify(g => g.IsRefusedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(DisconnectReason.Superseded, closing.CloseRequest?.Reason);
    }

    private FakeClientConnection Hold(Guid clientRefId)
    {
        var connection = new FakeClientConnection { ClientId = 1, WorkspaceId = 2, TokenIssuedAtUtc = IssuedAt };
        _connections.Register(clientRefId, connection);
        return connection;
    }

    private AccessRecheck Recheck() => new(
        _connections,
        _gate.Object,
        Options.Create(new AccessRecheckOptions()),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<AccessRecheck>.Instance);
}
