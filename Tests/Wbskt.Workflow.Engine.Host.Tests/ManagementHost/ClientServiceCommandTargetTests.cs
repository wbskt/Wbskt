using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Commands are delivered live or not at all, so resolving a command's target is where an offline
/// device turns into DEVICE_OFFLINE instead of a command that is accepted and then goes nowhere.
/// </summary>
public sealed class ClientServiceCommandTargetTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClientService CreateService(ClientDetail? client)
    {
        var provider = new Mock<IClientProvider>();
        var lookup = provider.Setup(x => x.FindDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()));
        if (client is null)
        {
            lookup.ReturnsAsync((ClientDetail?)null);
        }
        else
        {
            lookup.ReturnsAsync(client);
        }

        return new ClientService(provider.Object, Mock.Of<IEventBus>(), Mock.Of<IClientTokenCutoffs>(), NullLogger<ClientService>.Instance);
    }

    private static ClientDetail Client(bool connected, string? hostId, int workspaceId = WorkspaceId)
    {
        return new ClientDetail { Id = ClientId, RefId = ClientRefId, WorkspaceId = workspaceId, IsConnected = connected, ConnectedHostId = hostId };
    }

    [Fact]
    public async Task A_connected_device_resolves_to_its_id_and_socket_host()
    {
        var result = await CreateService(Client(connected: true, hostId: "socket-a")).ResolveCommandTargetAsync(WorkspaceId, ClientRefId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ClientCommandTarget(ClientId, "socket-a"));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "socket-a")]
    [InlineData(true, null)]
    public async Task An_offline_device_is_a_DEVICE_OFFLINE_conflict(bool connected, string? hostId)
    {
        var result = await CreateService(Client(connected, hostId)).ResolveCommandTargetAsync(WorkspaceId, ClientRefId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DEVICE_OFFLINE");
        result.Error.Type.Should().Be(Wbskt.Infrastructure.ErrorType.Conflict);
    }

    [Fact]
    public async Task A_foreign_device_is_not_found_before_its_presence_is_revealed()
    {
        var result = await CreateService(Client(connected: false, hostId: null, workspaceId: 99)).ResolveCommandTargetAsync(WorkspaceId, ClientRefId);

        result.Error.Code.Should().Be("CLIENT_NOT_FOUND");
    }

    [Fact]
    public async Task An_unknown_device_is_not_found()
    {
        var result = await CreateService(null).ResolveCommandTargetAsync(WorkspaceId, ClientRefId);

        result.Error.Code.Should().Be("CLIENT_NOT_FOUND");
    }
}
