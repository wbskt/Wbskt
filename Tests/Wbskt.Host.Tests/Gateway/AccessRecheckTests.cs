using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Gateway.Core.HostedServices;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.Gateway;

public sealed class AccessRecheckTests
{
    private static readonly DateTime IssuedAt = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);
    private readonly RevocationCache _cache = new();
    private readonly Mock<IClientTokenCutoffs> _cutoffs = new();

    [Fact]
    public async Task A_connection_whose_token_a_cutoff_now_refuses_is_closed_as_revoked()
    {
        Guid revokedRef = Guid.NewGuid(), keptRef = Guid.NewGuid();
        var revoked = Hold(revokedRef);
        var kept = Hold(keptRef);
        _cutoffs.Setup(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([revokedRef]);

        await Recheck().TickAsync(CancellationToken.None);

        Assert.Equal(DisconnectReason.Revoked, revoked.CloseRequest?.Reason);
        Assert.Null(kept.CloseRequest);
        _cutoffs.Verify(c => c.FindRefusedAsync(
            It.Is<IReadOnlyCollection<ClientToken>>(t => t.Count == 2 && t.All(x => x.IssuedAt == IssuedAt)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task With_redis_unable_to_answer_only_this_hosts_list_decides()
    {
        Guid listedRef = Guid.NewGuid();
        var listed = Hold(listedRef);
        var other = Hold(Guid.NewGuid());
        _cache.Revoke(listedRef);
        _cutoffs.Setup(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid>?)null);

        await Recheck().TickAsync(CancellationToken.None);

        Assert.Equal(DisconnectReason.Revoked, listed.CloseRequest?.Reason);
        Assert.Null(other.CloseRequest);
    }

    [Fact]
    public async Task A_token_issued_after_a_reinstatement_is_kept_and_an_older_one_closed()
    {
        Guid clientRef = Guid.NewGuid();
        var older = Hold(clientRef, IssuedAt);
        _cache.Reinstate(clientRef, IssuedAt.AddMinutes(5));
        _cutoffs.Setup(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Recheck().TickAsync(CancellationToken.None);
        Assert.Equal(DisconnectReason.Revoked, older.CloseRequest?.Reason);

        var fresh = Hold(clientRef, IssuedAt.AddMinutes(5));
        await Recheck().TickAsync(CancellationToken.None);
        Assert.Null(fresh.CloseRequest);
    }

    [Fact]
    public async Task Only_a_revoke_this_host_never_received_counts_as_missed()
    {
        Guid missedRef = Guid.NewGuid(), lateRef = Guid.NewGuid();
        Hold(missedRef);
        var late = Hold(lateRef);
        _cutoffs.Setup(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _cache.Revoke(lateRef))
            .ReturnsAsync([missedRef, lateRef]);
        var counted = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "wbskt_socket_access_recheck_closed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => counted.Add(value));
        listener.Start();

        await Recheck().TickAsync(CancellationToken.None);

        Assert.Equal(DisconnectReason.Revoked, late.CloseRequest?.Reason);
        Assert.Equal([1L], counted.Where(v => v > 0));
    }

    [Fact]
    public async Task A_close_that_throws_does_not_stop_the_rest()
    {
        Guid failingRef = Guid.NewGuid(), nextRef = Guid.NewGuid();
        var failing = Hold(failingRef);
        failing.CloseFailure = new InvalidOperationException("gone");
        var next = Hold(nextRef);
        _cutoffs.Setup(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([failingRef, nextRef]);

        await Recheck().TickAsync(CancellationToken.None);

        Assert.Equal(DisconnectReason.Revoked, next.CloseRequest?.Reason);
    }

    [Fact]
    public async Task A_connection_already_closing_or_closed_is_not_checked()
    {
        var closing = Hold(Guid.NewGuid());
        await closing.RequestCloseAsync(DisconnectReason.Superseded, "newer");
        var closed = Hold(Guid.NewGuid());
        closed.Open = false;

        await Recheck().TickAsync(CancellationToken.None);

        _cutoffs.Verify(c => c.FindRefusedAsync(It.IsAny<IReadOnlyCollection<ClientToken>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(DisconnectReason.Superseded, closing.CloseRequest?.Reason);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5, 5)]
    [InlineData(60, 1)]
    public void An_interval_out_of_range_falls_back_to_the_default(int minutes, int expected)
    {
        Assert.Equal(TimeSpan.FromMinutes(expected), new AccessRecheckOptions { Interval = TimeSpan.FromMinutes(minutes) }.EffectiveInterval);
    }

    private FakeClientConnection Hold(Guid clientRefId, DateTime? issuedAt = null)
    {
        var connection = new FakeClientConnection { ClientId = 1, WorkspaceId = 2, TokenIssuedAtUtc = issuedAt ?? IssuedAt };
        _connections.Register(clientRefId, connection);
        return connection;
    }

    private AccessRecheck Recheck() => new(
        _connections,
        new ClientAccessGate(_cache, _cutoffs.Object, _connections),
        new GatewayMetrics(GatewayTransport.WebSocket),
        Options.Create(new AccessRecheckOptions()),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<AccessRecheck>.Instance);
}
