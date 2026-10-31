using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>Deleting a policy (#197): refused while clients are enrolled through it, announced when done.</summary>
public sealed class PolicyDeletionTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid PolicyRef = Guid.NewGuid();

    private readonly Mock<IRegistrationPolicyProvider> _provider = new();
    private readonly Mock<IEventBus> _bus = new();

    [Fact]
    public async Task A_policy_with_no_clients_is_deleted_and_announced()
    {
        _provider.Setup(p => p.DeleteAsync(WorkspaceId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new PolicyDeleteOutcome(true, 0));

        var result = await Service(Policy(WorkspaceId)).DeleteAsync(WorkspaceId, PolicyRef);

        result.IsSuccess.Should().BeTrue();
        _bus.Verify(b => b.PublishAsync(It.Is<PolicyDeletedEvent>(e => e.PolicyRefId == PolicyRef && e.PolicyId == 5 && e.WorkspaceId == WorkspaceId && e.Name == "lab"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, "1 client.")]
    [InlineData(3, "3 clients.")]
    public async Task A_policy_with_clients_is_refused_with_how_many(int clients, string says)
    {
        _provider.Setup(p => p.DeleteAsync(WorkspaceId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new PolicyDeleteOutcome(false, clients));

        var result = await Service(Policy(WorkspaceId)).DeleteAsync(WorkspaceId, PolicyRef);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POLICY_HAS_CLIENTS");
        result.Error.Message.Should().Contain(says);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Another_workspaces_policy_is_not_found_and_not_touched()
    {
        var result = await Service(Policy(WorkspaceId + 1)).DeleteAsync(WorkspaceId, PolicyRef);

        result.Error.Code.Should().Be("POLICY_NOT_FOUND");
        _provider.Verify(p => p.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_policy_deleted_meanwhile_is_not_found()
    {
        _provider.Setup(p => p.DeleteAsync(WorkspaceId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new PolicyDeleteOutcome(false, 0));

        var result = await Service(Policy(WorkspaceId)).DeleteAsync(WorkspaceId, PolicyRef);

        result.Error.Code.Should().Be("POLICY_NOT_FOUND");
        _bus.VerifyNoOtherCalls();
    }

    private RegistrationPolicyService Service(RegistrationPolicy policy)
    {
        _provider.Setup(p => p.FindByRefIdAsync(PolicyRef, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        return new RegistrationPolicyService(_provider.Object, Mock.Of<IClientProvider>(), _bus.Object, NullLogger<RegistrationPolicyService>.Instance);
    }

    private static RegistrationPolicy Policy(int workspaceId) => new() { Id = 5, RefId = PolicyRef, WorkspaceId = workspaceId, Name = "lab", IsEnabled = true };
}
