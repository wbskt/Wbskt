using Wbskt.Protocol;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Models.Billing;
using Wbskt.Host.Tests.AuthHost;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// A client registering into a workspace whose plan is already at its limit on registered clients:
/// <c>dbo.Client_Create</c> refuses it with 50025, and the API answers 409 with the plan's number.
/// </summary>
public sealed class ClientRegistrationServiceTests
{
    private const int WorkspaceId = 7;

    [Fact]
    public async Task A_registration_the_plan_has_no_room_for_is_a_409_naming_the_plan_its_limit_and_the_clients_held()
    {
        var clients = new Mock<IClientProvider>();
        var policies = new Mock<IRegistrationPolicyProvider>();
        var bus = new Mock<IEventBus>();
        var policy = new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "lab", IsEnabled = true, AutoApproval = true };
        policies.Setup(p => p.FindByPinAsync("PIN", It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        clients
            .Setup(p => p.InsertClientAsync(WorkspaceId, policy.Id, "sensor", It.IsAny<byte[]>(), ClientStatus.Registered, new PlanCeiling(25, 3), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50025, "The plan has reached its client limit."));
        clients.Setup(p => p.GetRegisteredCountByAccountAsync(WorkspaceId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(140);
        var plans = new FixedWorkspacePlans(PlanCode.Maker, tenantId: 3, adjust: l => l with { Clients = 25 });
        using var metrics = new ManagementMetrics();
        var service = new ClientRegistrationService(clients.Object, policies.Object, bus.Object, NullLogger<ClientRegistrationService>.Instance, metrics, plans);
        var controller = new ClientRegistrationsController(service, NullLogger<ClientRegistrationsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = (await controller.Initiate(new ClientRegistrationRequest("PIN", "sensor"), CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status409Conflict).Should().Be("PLAN_CLIENT_LIMIT_REACHED");
        ProblemAssert.Detail(result).Should().Contain("You have 140 clients; Maker allows 25");
        var problem = (ProblemDetails)((ObjectResult)result!).Value!;
        problem.Extensions["plan"].Should().Be("Maker");
        problem.Extensions["limit"].Should().Be(25L);
        problem.Extensions["current"].Should().Be(140L);
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientRegistrationInitiatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        // #988: the refusal shows in the policy's activity and on the console, not only to the board.
        bus.Verify(b => b.PublishAsync(
            It.Is<PlanClientLimitReachedEvent>(e => e.PolicyRefId == policy.RefId && e.PolicyId == policy.Id && e.WorkspaceId == WorkspaceId
                && e.PlanCode == "Maker" && e.Limit == 25 && e.Current == 140 && e.ClientRefId == Guid.Empty && e.ClientName == "sensor"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_frozen_account_enrols_nothing(bool seenFirst)
    {
        var clients = new Mock<IClientProvider>();
        var policies = new Mock<IRegistrationPolicyProvider>();
        var bus = new Mock<IEventBus>();
        var policy = new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "lab", IsEnabled = true, AutoApproval = true };
        policies.Setup(p => p.FindByPinAsync("PIN", It.IsAny<CancellationToken>())).ReturnsAsync(policy);

        // Either this host's copy already says so, or the freeze lands after it was read and Client_Create refuses.
        clients
            .Setup(p => p.InsertClientAsync(WorkspaceId, policy.Id, "sensor", It.IsAny<byte[]>(), ClientStatus.Registered, It.IsAny<PlanCeiling>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50044, "The account is frozen."));
        using var metrics = new ManagementMetrics();
        var service = new ClientRegistrationService(clients.Object, policies.Object, bus.Object, NullLogger<ClientRegistrationService>.Instance, metrics, new FixedWorkspacePlans(PlanCode.Pro, tenantId: 3, frozen: seenFirst));

        var result = await service.InitiateRegistrationAsync(new ClientRegistrationRequest("PIN", "sensor"), CancellationToken.None);

        result.Error.Code.Should().Be("ACCOUNT_FROZEN");
        clients.Verify(p => p.InsertClientAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ClientStatus>(), It.IsAny<PlanCeiling>(), It.IsAny<CancellationToken>()), seenFirst ? Times.Never() : Times.Once());
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientRegistrationInitiatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_policy_at_its_limit_refuses_on_the_count_its_lookup_read_without_counting_again()
    {
        var clients = new Mock<IClientProvider>(MockBehavior.Strict);
        var policies = new Mock<IRegistrationPolicyProvider>();
        var bus = new Mock<IEventBus>();
        var policy = new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "lab", IsEnabled = true, AutoApproval = true, MaxClients = 3, RegisteredClientCount = 3 };
        policies.Setup(p => p.FindByPinAsync("PIN", It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        using var metrics = new ManagementMetrics();
        var service = new ClientRegistrationService(clients.Object, policies.Object, bus.Object, NullLogger<ClientRegistrationService>.Instance, metrics, new FixedWorkspacePlans());

        var result = await service.InitiateRegistrationAsync(new ClientRegistrationRequest("PIN", "sensor"), CancellationToken.None);

        result.Error.Code.Should().Be("POLICY_LIMIT_REACHED");
        clients.VerifyNoOtherCalls();
    }
}
