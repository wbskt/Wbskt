using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Models;
using ClientRow = Wbskt.Management.Host.Models.Client;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientServiceRenameTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly IClientTokenCutoffs NoCutoffs = Mock.Of<IClientTokenCutoffs>();

    private static (ClientService Service, Mock<IClientProvider> Provider, Mock<IEventBus> Bus) CreateService(ClientRow client)
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(x => x.GetByIdAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var bus = new Mock<IEventBus>();
        var service = new ClientService(provider.Object, Mock.Of<IRegistrationPolicyProvider>(), bus.Object, NoCutoffs, NullLogger<ClientService>.Instance);
        return (service, provider, bus);
    }

    private static ClientRow CreateClient(int workspaceId = WorkspaceId, string name = "old-name")
    {
        return new ClientRow { Id = ClientId, RefId = ClientRefId, WorkspaceId = workspaceId, Name = name };
    }

    [Fact]
    public async Task Rename_updates_and_publishes_event_with_old_name()
    {
        var (service, provider, bus) = CreateService(CreateClient());

        var result = await service.RenameAsync(WorkspaceId, ClientId, "new-name");

        result.IsSuccess.Should().BeTrue();
        provider.Verify(x => x.UpdateNameAsync(ClientId, "new-name", It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientRenamedEvent>(e => e.ClientRefId == ClientRefId && e.OldName == "old-name" && e.NewName == "new-name"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rename_rejects_client_from_another_workspace()
    {
        var (service, provider, bus) = CreateService(CreateClient(workspaceId: 99));

        var result = await service.RenameAsync(WorkspaceId, ClientId, "new-name");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CLIENT_UNAUTHORIZED");
        provider.Verify(x => x.UpdateNameAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientRenamedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rename_to_the_same_name_is_a_no_op()
    {
        var (service, provider, bus) = CreateService(CreateClient(name: "same-name"));

        var result = await service.RenameAsync(WorkspaceId, ClientId, "same-name");

        result.IsSuccess.Should().BeTrue();
        provider.Verify(x => x.UpdateNameAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientRenamedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureClientInWorkspace_returns_the_internal_id_when_owned()
    {
        var (service, _, _) = CreateServiceWithDetail(CreateDetail());

        var result = await service.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ClientId);
    }

    [Fact]
    public async Task EnsureClientInWorkspace_rejects_a_client_from_another_workspace()
    {
        var (service, _, _) = CreateServiceWithDetail(CreateDetail(workspaceId: 99));

        var result = await service.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CLIENT_UNAUTHORIZED");
    }

    /// <summary>
    /// An unknown reference and one belonging to another workspace have to be indistinguishable, or
    /// the endpoint confirms that a guessed reference names a real client somewhere.
    /// </summary>
    [Fact]
    public async Task EnsureClientInWorkspace_reports_an_unknown_reference_exactly_as_a_foreign_one()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(x => x.GetDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no rows"));
        var service = new ClientService(provider.Object, Mock.Of<IRegistrationPolicyProvider>(), Mock.Of<IEventBus>(), NoCutoffs, NullLogger<ClientService>.Instance);
        var (foreignService, _, _) = CreateServiceWithDetail(CreateDetail(workspaceId: 99));

        var unknown = await service.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId);
        var foreign = await foreignService.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId);

        unknown.IsFailure.Should().BeTrue();
        unknown.Error.Should().Be(foreign.Error);
    }

    private static (ClientService Service, Mock<IClientProvider> Provider, Mock<IEventBus> Bus) CreateServiceWithDetail(ClientDetail detail)
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(x => x.GetDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        var bus = new Mock<IEventBus>();
        var service = new ClientService(provider.Object, Mock.Of<IRegistrationPolicyProvider>(), bus.Object, NoCutoffs, NullLogger<ClientService>.Instance);
        return (service, provider, bus);
    }

    private static ClientDetail CreateDetail(int workspaceId = WorkspaceId)
    {
        return new ClientDetail { Id = ClientId, RefId = ClientRefId, WorkspaceId = workspaceId, Name = "client" };
    }
}
