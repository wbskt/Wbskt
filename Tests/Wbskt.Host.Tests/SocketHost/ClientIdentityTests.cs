using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.SocketHost;

public sealed class ClientIdentityTests
{
    private static readonly Guid ClientRefId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    [Fact]
    public void A_device_token_is_read_in_full()
    {
        var principal = Principal(Claims());

        Assert.True(ClientIdentity.TryRead(principal, out var identity));
        Assert.Equal(new ClientIdentity(ClientRefId, 42, 7, DateTimeOffset.FromUnixTimeSeconds(1_760_000_000).UtcDateTime), identity);
    }

    [Fact]
    public void A_device_token_from_before_the_claims_contract_is_still_read()
    {
        // #306: sub under its mapped name and the old "type" claim, for one token lifetime.
        Claim[] legacy =
        [
            new(ClaimTypes.NameIdentifier, ClientRefId.ToString()),
            new(ClientClaims.Id, "42"),
            new(ClientClaims.WorkspaceId, "7"),
            new("type", WbsktClaims.ClientType),
            new(ClientClaims.IssuedAt, "1760000000")
        ];

        Assert.True(ClientIdentity.TryRead(Principal(legacy), out var identity));
        Assert.Equal(ClientRefId, identity.ClientRefId);
    }

    [Theory]
    [InlineData(WbsktClaims.Type)]
    [InlineData(WbsktClaims.Subject)]
    [InlineData(ClientClaims.Id)]
    [InlineData(ClientClaims.WorkspaceId)]
    public void A_token_missing_a_claim_is_refused_rather_than_failing_later(string missing)
    {
        Assert.False(ClientIdentity.TryRead(Principal(Claims().Where(c => c.Type != missing)), out _));
    }

    [Fact]
    public void A_user_token_is_not_a_device()
    {
        var claims = Claims().Select(c => c.Type == WbsktClaims.Type ? new Claim(WbsktClaims.Type, WbsktClaims.UserType) : c);

        Assert.False(ClientIdentity.TryRead(Principal(claims), out _));
    }

    [Fact]
    public void A_token_without_an_issue_time_reads_as_older_than_any_cutoff()
    {
        Assert.True(ClientIdentity.TryRead(Principal(Claims().Where(c => c.Type != ClientClaims.IssuedAt)), out var identity));
        Assert.Equal(DateTime.MinValue, identity.IssuedAtUtc);
    }

    [Theory]
    [InlineData("/ws", null, "query-token")]
    [InlineData("/ws", "Bearer header-token", null)]
    [InlineData("/api/clients", null, null)]
    public async Task The_query_token_is_read_only_on_the_paths_given_and_never_over_a_header(string path, string? header, string? expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.QueryString = new QueryString("?access_token=query-token");
        if (header != null)
        {
            http.Request.Headers.Authorization = header;
        }

        var events = new JwtBearerEvents().AcceptQueryToken("/ws");
        var context = new MessageReceivedContext(http, new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), new JwtBearerOptions());

        await events.OnMessageReceived(context);

        Assert.Equal(expected, context.Token);
    }

    private static IEnumerable<Claim> Claims() =>
    [
        .. ClientClaims.For(ClientRefId, "pump", Guid.NewGuid(), clientId: 42, workspaceId: 7),
        new(ClientClaims.IssuedAt, "1760000000")
    ];

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) => new(new ClaimsIdentity(claims, "test"));
}
