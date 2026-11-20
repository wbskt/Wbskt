using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// A known client's refused sign-ins are counted per client and raised once a minute with the count, so a
/// device looping on a revoked credential shows in its workspace without flooding the log (#669).
/// </summary>
public sealed class ClientSignInRefusalTests
{
    private const string Secret = "the-right-secret";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ClientStatus.Revoked, ClientSignInRefusal.Revoked)]
    [InlineData(ClientStatus.Rejected, ClientSignInRefusal.Rejected)]
    public async Task A_client_that_may_not_connect_is_counted_with_its_status(ClientStatus status, ClientSignInRefusal reason)
    {
        var refusals = new ClientSignInRefusals();
        Client client = Client(status);

        await LoginAsync(refusals, client, Secret);
        await LoginAsync(refusals, client, Secret);

        var refused = Assert.Single(refusals.Take());
        Assert.Equal((client.RefId, client.Id, client.WorkspaceId, reason, 2), (refused.ClientRefId, refused.ClientId, refused.WorkspaceId, refused.Reason, refused.Attempts));
        Assert.Empty(refusals.Take());
    }

    [Fact]
    public async Task A_wrong_secret_for_a_known_client_is_counted()
    {
        var refusals = new ClientSignInRefusals();

        var result = await LoginAsync(refusals, Client(ClientStatus.Registered), "wrong");

        Assert.Equal("CLIENT_UNAUTHORIZED", result.Error.Code);
        Assert.Equal(ClientSignInRefusal.BadSecret, Assert.Single(refusals.Take()).Reason);
    }

    [Fact]
    public async Task An_unknown_client_a_pending_one_and_a_good_sign_in_are_not_counted()
    {
        var refusals = new ClientSignInRefusals();

        await LoginAsync(refusals, null, Secret);
        await LoginAsync(refusals, Client(ClientStatus.Pending), Secret);
        var accepted = await LoginAsync(refusals, Client(ClientStatus.Registered), Secret);

        Assert.True(accepted.IsSuccess);
        Assert.Empty(refusals.Take());
    }

    [Fact]
    public void The_count_keeps_the_first_and_last_attempt_and_the_latest_reason()
    {
        var refusals = new ClientSignInRefusals();
        Guid refId = Guid.NewGuid();

        refusals.Record(5, refId, 2, ClientSignInRefusal.BadSecret, Now);
        refusals.Record(5, refId, 2, ClientSignInRefusal.Revoked, Now.AddSeconds(40));
        refusals.Record(6, Guid.NewGuid(), 2, ClientSignInRefusal.Rejected, Now.AddSeconds(50));

        var taken = refusals.Take().ToDictionary(e => e.ClientId);
        var first = taken[5];
        Assert.Equal((refId, 2, ClientSignInRefusal.Revoked, 2, Now, Now.AddSeconds(40)), (first.ClientRefId, first.WorkspaceId, first.Reason, first.Attempts, first.FirstAttemptAt, first.LastAttemptAt));
        Assert.Equal(1, taken[6].Attempts);
    }

    [Fact]
    public void Past_the_cap_a_new_client_waits_while_one_held_keeps_counting()
    {
        var refusals = new ClientSignInRefusals();
        for (int id = 0; id < ClientSignInRefusals.MaxClients; id++)
        {
            refusals.Record(id, Guid.NewGuid(), 1, ClientSignInRefusal.Revoked, Now);
        }

        refusals.Record(ClientSignInRefusals.MaxClients, Guid.NewGuid(), 1, ClientSignInRefusal.Revoked, Now);
        refusals.Record(0, Guid.NewGuid(), 1, ClientSignInRefusal.Revoked, Now);

        var taken = refusals.Take();
        Assert.Equal(ClientSignInRefusals.MaxClients, taken.Count);
        Assert.Equal(2, taken.Single(e => e.ClientId == 0).Attempts);
    }

    private static Client Client(ClientStatus status) => new()
    {
        Id = 41,
        RefId = Guid.NewGuid(),
        WorkspaceId = 3,
        PolicyRefId = Guid.NewGuid(),
        Name = "sensor-03",
        Status = status
    };

    private static async Task<Wbskt.Infrastructure.Result<ClientLoginResponse>> LoginAsync(ClientSignInRefusals refusals, Client? client, string secret)
    {
        var provider = new Mock<IClientProvider>();
        Guid refId = client?.RefId ?? Guid.NewGuid();
        provider.Setup(p => p.FindCredentialAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client is null ? null : new ClientCredential(client, ClientSecrets.Hash(Secret)));
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<System.Security.Claims.Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("token");
        using var metrics = new ManagementMetrics();
        var service = new ClientAuthService(provider.Object, jwt.Object, NullLogger<ClientAuthService>.Instance, metrics, refusals, new FixedClock());
        return await service.LoginAsync(new ClientLoginRequest(refId, secret));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
