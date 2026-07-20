using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using ClientRow = Wbskt.Management.Host.Models.Client;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientServiceRenameTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (ClientService Service, Mock<IClientProvider> Provider, Mock<IEventBus> Bus) CreateService(ClientRow client)
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(x => x.GetByIdAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var bus = new Mock<IEventBus>();
        var service = new ClientService(provider.Object, Mock.Of<IRegistrationPolicyProvider>(), bus.Object, NullLogger<ClientService>.Instance);
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
}
