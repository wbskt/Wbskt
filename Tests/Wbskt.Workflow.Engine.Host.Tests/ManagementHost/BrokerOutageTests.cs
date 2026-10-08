using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Events.Management;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// With RabbitMQ down, a change that has already been committed still gets its normal answer: a
/// rotated secret is handed back rather than lost behind a 500, and a registering device receives
/// its secret rather than retrying into a second client. The events go out once the broker is back.
/// Commands and pings are the exception, because their publish is the action: those report the
/// outage as a 503.
/// </summary>
public sealed class BrokerOutageTests
{
    private const int WorkspaceId = 7;

    [Fact]
    public async Task Rotating_a_secret_returns_it_while_the_broker_is_down_and_announces_it_later()
    {
        await using var harness = new Harness();
        var client = harness.AddClient();
        harness.Clients
            .Setup(p => p.UpdateSecretAsync(client.Id, WorkspaceId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var announced = harness.Expect<ClientSecretRotatedEvent>();

        var result = await harness.Resolve<IClientService>().RotateSecretAsync(WorkspaceId, client.RefId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Secret.Should().NotBeNullOrEmpty();

        harness.BrokerComesBack();
        (await announced.WaitAsync(TimeSpan.FromSeconds(5))).ClientRefId.Should().Be(client.RefId);
    }

    [Fact]
    public async Task Registering_returns_the_secret_while_the_broker_is_down_and_announces_it_later()
    {
        await using var harness = new Harness();
        var policy = new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "lab", IsEnabled = true, AutoApproval = true };
        harness.Policies.Setup(p => p.FindByPinAsync("PIN", It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        harness.Clients
            .Setup(p => p.InsertClientAsync(WorkspaceId, policy.Id, "sensor", It.IsAny<byte[]>(), ClientStatus.Registered, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wbskt.Management.Host.Models.Client { Id = 50, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, PolicyId = policy.Id, Name = "sensor", Status = ClientStatus.Registered });
        var announced = harness.Expect<ClientAutoApprovedEvent>();

        var result = await harness.Resolve<IClientRegistrationService>().InitiateRegistrationAsync(new ClientRegistrationRequest("PIN", "sensor"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Secret.Should().NotBeNullOrEmpty();
        harness.Clients.Verify(p => p.InsertClientAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ClientStatus>(), It.IsAny<CancellationToken>()), Times.Once);

        harness.BrokerComesBack();
        (await announced.WaitAsync(TimeSpan.FromSeconds(5))).PolicyId.Should().Be(policy.Id);
    }

    [Fact]
    public async Task A_command_reports_an_unavailable_broker_as_503()
    {
        var clientRef = Guid.NewGuid();
        var clientService = new Mock<IClientService>();
        clientService.Setup(x => x.ResolveCommandTargetAsync(WorkspaceId, clientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Wbskt.Management.Host.Models.ClientCommandTarget>.Success(new Wbskt.Management.Host.Models.ClientCommandTarget(42, "socket-a")));
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        var controller = new ClientsController(
            clientService.Object, bus.Object,
            Mock.Of<IRegistrationPolicyService>(), Mock.Of<IEventLogService>(), NullLogger<ClientsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.SendCommand(WorkspaceId, clientRef, new ClientCommandRequest("reboot", "{}"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        controller.Response.Headers.RetryAfter.ToString().Should().Be("5");
    }

    /// <summary>
    /// The services wired as the host wires them, onto a real bus that fails until
    /// <see cref="BrokerComesBack"/>.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly QueuedEventDispatcher _dispatcher;
        private readonly Mock<IEventBus> _bus = new();
        private volatile bool _brokerUp;
        private int _nextId = 40;

        public Harness()
        {
            Clients
                .Setup(p => p.FindDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ClientDetail?)null);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Clients.Object);
            services.AddSingleton(Policies.Object);
            services.AddSingleton(_bus.Object);
            services.AddSingleton(Mock.Of<IClientTokenCutoffs>());
            services.AddQueuedEventBus();
            services.AddScopedWithQueuedEvents<IClientService, ClientService>();
            services.AddScopedWithQueuedEvents<IClientRegistrationService, ClientRegistrationService>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            _dispatcher = new QueuedEventDispatcher(
                _provider.GetRequiredService<QueuedEventBus>(), _bus.Object, NullLogger<QueuedEventDispatcher>.Instance,
                Enumerable.Repeat(TimeSpan.FromMilliseconds(20), 500).ToArray());
            _dispatcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public Mock<IClientProvider> Clients { get; } = new();

        public Mock<IRegistrationPolicyProvider> Policies { get; } = new();

        public T Resolve<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

        public void BrokerComesBack() => _brokerUp = true;

        /// <summary>Makes every publish fail until the broker is back, and completes when <typeparamref name="TEvent"/> goes out.</summary>
        public Task<TEvent> Expect<TEvent>() where TEvent : IEvent
        {
            var sent = new TaskCompletionSource<TEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            _bus.Setup(b => b.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
                .Returns<TEvent, CancellationToken>((e, _) =>
                {
                    if (!_brokerUp)
                    {
                        return Task.FromException(new InvalidOperationException("broker down"));
                    }

                    sent.TrySetResult(e);
                    return Task.CompletedTask;
                });
            return sent.Task;
        }

        public ClientDetail AddClient()
        {
            var client = new ClientDetail
            {
                Id = ++_nextId,
                RefId = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                PolicyId = 5,
                PolicyRefId = Guid.NewGuid(),
                Name = $"device-{_nextId}",
                Status = ClientStatus.Registered
            };
            Clients.Setup(p => p.FindDetailByRefIdAsync(client.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _dispatcher.StopAsync(CancellationToken.None);
            _dispatcher.Dispose();
            await _provider.DisposeAsync();
        }
    }
}
