using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;
using Wbskt.Protocol;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// <c>PATCH /clients/{clientRef}</c> changes a client's name, status or both: the status first, and
/// a name it would refuse is refused before anything changes.
/// </summary>
public sealed class ClientUpdateTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRef = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IClientProvider> _provider = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly ClientLifecycleService _service;

    public ClientUpdateTests()
    {
        _provider.Setup(x => x.FindDetailByRefIdAsync(ClientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail { Id = ClientId, RefId = ClientRef, WorkspaceId = WorkspaceId, Name = "old", Status = ClientStatus.Pending });
        StatusOutcome(ClientStatusOutcome.Updated);
        _service = new ClientLifecycleService(_provider.Object, _bus.Object, Mock.Of<IClientTokenCutoffs>(), NullLogger<ClientLifecycleService>.Instance, new FixedWorkspacePlans(), TimeProvider.System,
            TestClientCommands.Queue(new InMemoryQueuedCommandStore(), Mock.Of<Wbskt.Infrastructure.Clients.IClientCommandTargetProvider>(), Mock.Of<IEventBus>()));
    }

    private void StatusOutcome(ClientStatusOutcome outcome)
    {
        _provider.Setup(x => x.UpdateStatusesAsync(WorkspaceId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<ClientStatus>(), It.IsAny<PlanCeiling>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClientStatusChange(ClientRef, ClientId, 1, Guid.NewGuid(), ClientStatus.Pending, outcome)]);
    }

    [Fact]
    public async Task Both_fields_change_in_one_request_with_the_name_trimmed()
    {
        var result = await _service.UpdateAsync(WorkspaceId, ClientRef, new UpdateClientRequest("  porch  ", ClientStatus.Registered));

        result.IsSuccess.Should().BeTrue();
        _provider.Verify(x => x.UpdateStatusesAsync(WorkspaceId, It.Is<IReadOnlyList<Guid>>(r => r.Single() == ClientRef), ClientStatus.Registered, It.IsAny<PlanCeiling>(), It.IsAny<CancellationToken>()), Times.Once);
        _provider.Verify(x => x.UpdateNameAsync(ClientId, "porch", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_request_naming_no_field_is_a_bad_request()
    {
        var result = await _service.UpdateAsync(WorkspaceId, ClientRef, new UpdateClientRequest(null, null));

        result.Error.Code.Should().Be("CLIENT_UPDATE_EMPTY");
        _provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task A_blank_name_changes_nothing_not_even_the_status(string name)
    {
        var result = await _service.UpdateAsync(WorkspaceId, ClientRef, new UpdateClientRequest(name, ClientStatus.Revoked));

        result.Error.Code.Should().Be("CLIENT_NAME_INVALID");
        _provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_name_over_100_characters_is_refused_rather_than_cut()
    {
        var result = await _service.UpdateAsync(WorkspaceId, ClientRef, new UpdateClientRequest(new string('a', 101), null));

        result.Error.Code.Should().Be("CLIENT_NAME_INVALID");
        _provider.Verify(x => x.UpdateNameAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_refused_status_leaves_the_name_alone()
    {
        StatusOutcome(ClientStatusOutcome.PolicyLimitReached);

        var result = await _service.UpdateAsync(WorkspaceId, ClientRef, new UpdateClientRequest("porch", ClientStatus.Registered));

        result.IsFailure.Should().BeTrue();
        _provider.Verify(x => x.UpdateNameAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientRenamedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
