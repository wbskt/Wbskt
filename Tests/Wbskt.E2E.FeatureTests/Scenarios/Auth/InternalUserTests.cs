using System.Net;
using FluentAssertions;
using Wbskt.Api.Client;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// <c>GET /api/internal/users/me</c>, which the management host calls to learn a user's internal id.
/// It is host-to-host (no public router serves <c>/api/internal</c>), so the typed client leaves it
/// out, but it still authenticates the caller's token rather than an internal key, and a test that
/// reaches the auth host directly can call it as the management host does.
///
/// Requires the auth host running. Skips gracefully when it is down.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class InternalUserTests(ServicesFixture fixture)
{
    // InternalRoutes.CurrentUser; the E2E project does not reference the hosts' infrastructure.
    private const string CurrentUser = "/api/internal/users/me";

    [SkippableFact]
    public async Task AUTH_INT_01_CurrentUser_AnswersTheCallersInternalId()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var first = await fixture.CreateUserAsync();
        var second = await fixture.CreateUserAsync();

        var firstId = await CurrentUserIdAsync(first.Token);
        var secondId = await CurrentUserIdAsync(second.Token);

        firstId.Should().BePositive();
        secondId.Should().BePositive().And.NotBe(firstId, "each token names its own user");
        (await CurrentUserIdAsync(first.Token)).Should().Be(firstId, "the id is stable for a user");
    }

    [SkippableFact]
    public async Task AUTH_INT_02_CurrentUser_WithoutAToken_Is401()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        using var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.AuthUrl(CurrentUser));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<int> CurrentUserIdAsync(string token)
    {
        using var response = await fixture.SendAsync(HttpMethod.Get, ServicesFixture.AuthUrl(CurrentUser), token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await WbsktApiClient.ReadAsync<CurrentUserResponse>(response)).UserId;
    }
}
