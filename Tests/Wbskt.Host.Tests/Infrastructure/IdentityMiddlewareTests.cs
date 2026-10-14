using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// A user token names its user by RefId only (#306); the internal id comes from the host's
/// <see cref="IUserIdLookup"/>, and a token issued before the change is read as it was.
/// </summary>
public sealed class IdentityMiddlewareTests
{
    private static readonly Guid UserRef = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid SessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IdentityService _identity = new();
    private readonly Mock<IUserIdLookup> _lookup = new();

    [Fact]
    public async Task A_current_token_is_resolved_to_the_hosts_id_by_its_reference()
    {
        _lookup.Setup(l => l.FindUserIdAsync(UserRef, It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var (seen, http) = await RunAsync(Principal(WbsktClaims.ForUser(UserRef, "ada", "ada@example.com", SessionId)));

        seen.Should().Be(new UserIdentity(5) { UserRefId = UserRef });
        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_token_from_before_the_claims_contract_carries_its_own_id()
    {
        var legacy = Principal(
            new Claim(ClaimTypes.NameIdentifier, "5"),
            new Claim("type", WbsktClaims.UserType),
            new Claim("uref", UserRef.ToString()));

        var (seen, _) = await RunAsync(legacy);

        seen.Should().Be(new UserIdentity(5) { UserRefId = UserRef });
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_user_the_host_cannot_find_is_no_one()
    {
        _lookup.Setup(l => l.FindUserIdAsync(UserRef, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);

        var (seen, _) = await RunAsync(Principal(WbsktClaims.ForUser(UserRef, "ada", "ada@example.com", SessionId)));

        seen.Should().BeNull();
    }

    [Fact]
    public async Task A_lookup_that_fails_is_a_503_not_a_sign_out()
    {
        _lookup.Setup(l => l.FindUserIdAsync(UserRef, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("auth down"));

        var (seen, http) = await RunAsync(Principal(WbsktClaims.ForUser(UserRef, "ada", "ada@example.com", SessionId)));

        seen.Should().BeNull();
        http.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        http.Response.Headers.RetryAfter.ToString().Should().Be(ApiErrorResults.RetryAfterSeconds);
        http.Response.ContentType.Should().StartWith(Problems.ContentType);
    }

    [Fact]
    public async Task A_device_token_is_not_a_user()
    {
        var (seen, _) = await RunAsync(Principal(ClientClaims.For(UserRef, "pump", Guid.NewGuid(), 42, 7)));

        seen.Should().BeNull();
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_found_id_is_asked_for_once()
    {
        _lookup.Setup(l => l.FindUserIdAsync(UserRef, It.IsAny<CancellationToken>())).ReturnsAsync(5);
        var cached = new CachedUserIdLookup(_lookup.Object, new MemoryCache(new MemoryCacheOptions()));

        (await cached.FindUserIdAsync(UserRef, CancellationToken.None)).Should().Be(5);
        (await cached.FindUserIdAsync(UserRef, CancellationToken.None)).Should().Be(5);

        _lookup.Verify(l => l.FindUserIdAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_issued_token_reads_the_same_whether_or_not_the_handler_maps_claims(bool mapInboundClaims)
    {
        // The bearer handler maps sub and sid to long names, a bare JsonWebTokenHandler does not.
        var key = new ECDsaSecurityKey(System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256));
        var handler = new JsonWebTokenHandler { MapInboundClaims = mapInboundClaims };
        string token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(WbsktClaims.ForUser(UserRef, "ada", "ada@example.com", SessionId)),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
            Issuer = "wbskt-auth",
            Audience = "wbskt-api"
        });

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidIssuer = "wbskt-auth",
            ValidAudience = "wbskt-api"
        });
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);

        WbsktClaims.TryReadUser(principal, out Guid userRef, out int? legacyId).Should().BeTrue();
        userRef.Should().Be(UserRef);
        legacyId.Should().BeNull();
        WbsktClaims.SessionOf(principal).Should().Be(SessionId);
        WbsktClaims.Find(principal, WbsktClaims.Email).Should().Be("ada@example.com");
    }

    [Fact]
    public void A_user_token_carries_no_internal_id()
    {
        var claims = WbsktClaims.ForUser(UserRef, "ada", "ada@example.com", SessionId);

        claims.Select(c => c.Type).Should().BeEquivalentTo([WbsktClaims.Subject, WbsktClaims.Type, WbsktClaims.Name, WbsktClaims.Email, WbsktClaims.Session]);
        claims.Single(c => c.Type == WbsktClaims.Subject).Value.Should().Be(UserRef.ToString());
    }

    private async Task<(UserIdentity? Seen, HttpContext Http)> RunAsync(ClaimsPrincipal principal)
    {
        UserIdentity? seen = null;
        var middleware = new IdentityMiddleware(_ =>
        {
            _identity.TryGetUserIdentity(out seen);
            return Task.CompletedTask;
        }, NullLogger<IdentityMiddleware>.Instance);

        var http = new DefaultHttpContext
        {
            User = principal,
            RequestServices = new ServiceCollection().AddSingleton(_lookup.Object).BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };

        await middleware.InvokeAsync(http, _identity);
        return (seen, http);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));
}
