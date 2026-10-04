using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// How the service answers each outcome of <c>dbo.RefreshToken_Rotate</c>. The procedure does the
/// retiring and storing in one transaction; what matters here is that only a rotation hands out a
/// pair, that only a replay ends the user's access tokens, and that an event failing to go out never
/// turns a committed rotation into an error.
/// </summary>
public sealed class RefreshRotationTests
{
    private static readonly User TheUser = new()
    {
        Id = 42,
        RefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Username = "someone",
        Email = "someone@example.test",
        IsActive = true
    };

    [Fact]
    public async Task A_rotation_returns_the_token_that_was_stored()
    {
        var harness = new Harness();
        RefreshToken? stored = null;
        harness.Provider
            .Setup(p => p.RotateRefreshTokenAsync("old", It.IsAny<RefreshToken>(), "10.0.0.1", It.IsAny<CancellationToken>()))
            .Callback<string, RefreshToken, string, CancellationToken>((_, replacement, _, _) => stored = replacement)
            .ReturnsAsync(new RefreshRotation(RefreshRotationOutcome.Rotated, 42, TheUser));

        var result = await harness.Service.RefreshTokenAsync("old", "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.NotNull(stored);
        Assert.Equal(stored!.Token, result.Value.RefreshToken);
        Assert.True(stored.Expires > DateTime.UtcNow.AddDays(6));
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_rotation_succeeds_even_when_its_event_cannot_be_published()
    {
        var harness = new Harness();
        harness.Bus
            .Setup(b => b.PublishAsync(It.IsAny<TokenRotatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        harness.Rotates(new RefreshRotation(RefreshRotationOutcome.Rotated, 42, TheUser));

        var result = await harness.QueuedService.RefreshTokenAsync("old", "10.0.0.1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_replay_ends_the_users_access_tokens_and_raises_an_alert()
    {
        var harness = new Harness();
        harness.Rotates(new RefreshRotation(RefreshRotationOutcome.Replayed, 42, null));

        var result = await harness.Service.RefreshTokenAsync("old", "10.0.0.1");

        Assert.Equal("AUTH_TOKEN_INACTIVE", result.Error.Code);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.Is<SecurityAlertEvent>(e => e.AlertType == "RefreshTokenReplay"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(RefreshRotationOutcome.Unknown, "AUTH_INVALID_TOKEN")]
    [InlineData(RefreshRotationOutcome.Expired, "AUTH_TOKEN_INACTIVE")]
    [InlineData(RefreshRotationOutcome.UserInactive, "AUTH_USER_INACTIVE")]
    [InlineData(RefreshRotationOutcome.Raced, "AUTH_TOKEN_INACTIVE")]
    public async Task Every_other_outcome_is_refused_without_ending_any_session(RefreshRotationOutcome outcome, string expectedCode)
    {
        var harness = new Harness();
        harness.Rotates(new RefreshRotation(outcome, outcome == RefreshRotationOutcome.Unknown ? null : 42, null));

        var result = await harness.Service.RefreshTokenAsync("old", "10.0.0.1");

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Provider.Verify(p => p.RevokeAllRefreshTokensForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var jwt = new Mock<IJwtService>();
            jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("access-token");

            Service = Create(jwt.Object, Bus.Object);
            QueuedService = Create(jwt.Object, new Wbskt.Auth.Host.Services.Events.QueuedEventBus(NullLogger<Wbskt.Auth.Host.Services.Events.QueuedEventBus>.Instance));
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public Mock<IAccessTokenRevocation> AccessTokens { get; } = new();

        public Mock<IEventBus> Bus { get; } = new();

        /// <summary>Publishes straight to <see cref="Bus"/>, so a test can see what was sent.</summary>
        public AuthService Service { get; }

        /// <summary>Publishes through the queue, as the host wires it.</summary>
        public AuthService QueuedService { get; }

        public void Rotates(RefreshRotation rotation) =>
            Provider
                .Setup(p => p.RotateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<RefreshToken>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rotation);

        private AuthService Create(IJwtService jwt, IEventBus bus) => new(
            Provider.Object,
            jwt,
            bus,
            Mock.Of<IAuthMailer>(),
            Options.Create(new AuthEmailOptions()),
            NullLogger<AuthService>.Instance,
            new AuthMetrics(),
            AccessTokens.Object,
            Options.Create(new AccessTokenOptions()),
            MailCooldownTests.InProcess());
    }
}
