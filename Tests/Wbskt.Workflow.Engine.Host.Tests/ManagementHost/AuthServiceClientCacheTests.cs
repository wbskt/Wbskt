using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// The management host remembers a successful workspace resolution for a short while, per user, so a
/// page of API calls costs one round trip to the auth host. Denials are never remembered.
/// </summary>
public sealed class AuthServiceClientCacheTests
{
    private static readonly Guid WorkspaceRef = Guid.NewGuid();

    [Fact]
    public async Task A_repeat_resolve_for_the_same_user_is_answered_from_the_cache()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, _) = CreateClient(auth);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            var first = await client.ResolveWorkspaceAsync(WorkspaceRef);
            var second = await client.ResolveWorkspaceAsync(WorkspaceRef, Permissions.ClientsRead);

            first.IsSuccess.Should().BeTrue();
            second.IsSuccess.Should().BeTrue();
            second.Value.Should().Be(42);
        }

        auth.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Users_do_not_share_entries()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, _) = CreateClient(auth);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef);
        }

        using (identity.BeginScope(new UserIdentity(8)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef);
        }

        auth.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_denial_is_asked_again_every_time()
    {
        // So someone who has just been given access does not wait for an entry to expire.
        var auth = new FakeAuthHost(HttpStatusCode.Forbidden);
        var (client, identity, _) = CreateClient(auth);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            (await client.ResolveWorkspaceAsync(WorkspaceRef)).IsFailure.Should().BeTrue();
            (await client.ResolveWorkspaceAsync(WorkspaceRef)).IsFailure.Should().BeTrue();
        }

        auth.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_cached_entry_still_enforces_the_permission_asked_for()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, _) = CreateClient(auth);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef, Permissions.ClientsRead);
            var denied = await client.ResolveWorkspaceAsync(WorkspaceRef, Permissions.ClientsManage);

            denied.IsFailure.Should().BeTrue();
            denied.Error.Code.Should().Be("PERMISSION_UNAUTHORIZED");
        }

        auth.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Without_an_identity_nothing_is_cached()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, _, _) = CreateClient(auth);

        await client.ResolveWorkspaceAsync(WorkspaceRef);
        await client.ResolveWorkspaceAsync(WorkspaceRef);

        auth.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Zero_seconds_turns_the_cache_off()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, _) = CreateClient(auth, seconds: 0);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef);
            await client.ResolveWorkspaceAsync(WorkspaceRef);
        }

        auth.Calls.Should().Be(2);
    }

    [Fact]
    public async Task An_announced_change_drops_what_was_cached()
    {
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, cache) = CreateClient(auth);

        using (identity.BeginScope(new UserIdentity(7)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef);
            cache.Clear();
            await client.ResolveWorkspaceAsync(WorkspaceRef);
        }

        auth.Calls.Should().Be(2);
    }

    [Fact]
    public async Task An_answer_that_was_in_flight_during_a_change_is_not_kept()
    {
        // The auth host answered from before the change, so storing it would undo the clear.
        var auth = new FakeAuthHost(HttpStatusCode.OK);
        var (client, identity, cache) = CreateClient(auth);
        auth.OnCall = () => { if (auth.Calls == 1) cache.Clear(); };

        using (identity.BeginScope(new UserIdentity(7)))
        {
            await client.ResolveWorkspaceAsync(WorkspaceRef);
            await client.ResolveWorkspaceAsync(WorkspaceRef);
        }

        auth.Calls.Should().Be(2);
    }

    private static (AuthServiceClient Client, IdentityService Identity, WorkspaceAccessCache Cache) CreateClient(FakeAuthHost auth, int seconds = 30)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [WorkspaceAccessCache.SecondsKey] = seconds.ToString() })
            .Build();
        var identity = new IdentityService();
        var http = new HttpClient(auth) { BaseAddress = new Uri("http://auth.test") };
        var cache = new WorkspaceAccessCache(configuration);
        var client = new AuthServiceClient(http, NullLogger<AuthServiceClient>.Instance, cache, identity);
        return (client, identity, cache);
    }

    private sealed class FakeAuthHost(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Action? OnCall { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            OnCall?.Invoke();
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
            {
                response.Content = JsonContent.Create(new ResolvedWorkspaceResponse(42, [Permissions.ClientsRead.ToString()]));
            }

            return Task.FromResult(response);
        }
    }
}
