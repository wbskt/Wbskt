using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// Device, policy and workflow actions taken through the API record who took them. The actor is
/// stamped as the event is published, while the request's identity is still in scope.
/// </summary>
public sealed class ActorStampingEventBusTests
{
    private static readonly Guid UserRef = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly RecordingBus _inner = new();
    private readonly IdentityService _identity = new();

    private ActorStampingEventBus CreateBus() => new(_inner, _identity);

    private static ClientRenamedEvent Renamed() => new(Guid.NewGuid(), 42, 7, "old", "new");

    [Fact]
    public async Task An_action_taken_by_a_signed_in_user_records_them()
    {
        using (_identity.BeginScope(new UserIdentity(5) { UserRefId = UserRef }))
        {
            await CreateBus().PublishAsync(Renamed());
        }

        var published = _inner.Published.Should().ContainSingle().Which.Should().BeOfType<ClientRenamedEvent>().Subject;
        published.ActorUserId.Should().Be(5);
        published.ActorUserRefId.Should().Be(UserRef);
    }

    [Fact]
    public async Task An_event_raised_without_a_user_has_no_actor()
    {
        await CreateBus().PublishAsync(Renamed());

        var published = _inner.Published.Should().ContainSingle().Which.Should().BeOfType<ClientRenamedEvent>().Subject;
        published.ActorUserId.Should().BeNull();
        published.ActorUserRefId.Should().BeNull();
    }

    [Fact]
    public async Task An_actor_already_set_is_kept()
    {
        var @event = Renamed();
        @event.ActorUserId = 9;

        using (_identity.BeginScope(new UserIdentity(5)))
        {
            await CreateBus().PublishAsync(@event);
        }

        @event.ActorUserId.Should().Be(9);
    }

    [Fact]
    public async Task Events_that_are_not_actions_pass_through()
    {
        var @event = new WorkflowRunStartedEvent(Guid.NewGuid(), 3, Guid.NewGuid(), 7);

        using (_identity.BeginScope(new UserIdentity(5)))
        {
            await CreateBus().PublishAsync(@event);
        }

        _inner.Published.Should().ContainSingle().Which.Should().BeSameAs(@event);
    }

    [Theory]
    [InlineData("https://console.wbskt.dev", EventSource.Console)]
    [InlineData("https://elsewhere.example", EventSource.Api)]
    [InlineData(null, EventSource.Api)]
    public async Task An_action_records_where_it_came_from_and_the_callers_address(string? origin, EventSource expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("81.2.69.160");
        context.Request.Headers.UserAgent = "Mozilla/5.0 (Macintosh) Firefox/131.0";
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "https://console.wbskt.dev" })
            .Build();
        var accessor = new HttpRequestOriginAccessor(new HttpContextAccessor { HttpContext = context }, configuration);
        var @event = Renamed();

        using (_identity.BeginScope(new UserIdentity(5)))
        {
            await new ActorStampingEventBus(_inner, _identity, accessor).PublishAsync(@event);
        }

        @event.ActorSource.Should().Be(expected);
        @event.ClientAddress.Should().Be("81.2.69.160");
        @event.UserAgent.Should().Be("Mozilla/5.0 (Macintosh) Firefox/131.0");
    }

    [Fact]
    public async Task An_action_a_workflow_run_took_keeps_the_run_as_its_actor()
    {
        var @event = Renamed();
        @event.ActorSource = EventSource.Workflow;

        using (_identity.BeginScope(new UserIdentity(5)))
        {
            await CreateBus().PublishAsync(@event);
        }

        @event.ActorSource.Should().Be(EventSource.Workflow);
        @event.ActorUserId.Should().BeNull("a run is never attributed to whoever happens to be signed in");
    }

    [Fact]
    public async Task The_queued_bus_and_the_direct_bus_both_stamp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdentityService>(_identity);
        services.AddSingleton<IEventBus>(_inner);
        services.AddActorStampingEventBus();
        services.AddQueuedEventBus();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var direct = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var queued = scope.ServiceProvider.GetRequiredKeyedService<IEventBus>(QueuedEventBusExtensions.QueuedKey);
        var viaDirect = Renamed();
        var viaQueue = Renamed();
        using (_identity.BeginScope(new UserIdentity(5)))
        {
            await direct.PublishAsync(viaDirect);
            await queued.PublishAsync(viaQueue);
        }

        viaDirect.ActorUserId.Should().Be(5);
        viaQueue.ActorUserId.Should().Be(5, "the queue is drained later, after the request's identity is gone");
    }

    [Fact]
    public async Task The_identity_carries_the_user_reference_from_the_token()
    {
        UserIdentity? seen = null;
        var middleware = new IdentityMiddleware(_ =>
        {
            _identity.TryGetUserIdentity(out seen);
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "5"),
                new Claim(JwtServiceCollectionExtensions.JwtUserRefClaim, UserRef.ToString())
            ], "test"))
        };

        await middleware.InvokeAsync(context, _identity);

        seen.Should().NotBeNull();
        seen!.UserId.Should().Be(5);
        seen.UserRefId.Should().Be(UserRef);
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
