using Wbskt.Protocol;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Host.Tests.AuthHost;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// A device registering into a workspace that is already at its ceiling on registered clients:
/// <c>dbo.Client_Create</c> refuses it with 50025, and the API answers 409 with the configured number.
/// </summary>
public sealed class ClientRegistrationServiceTests
{
    private const int WorkspaceId = 7;

    [Fact]
    public async Task A_registration_the_workspace_has_no_room_for_is_a_409_naming_the_configured_number()
    {
        var clients = new Mock<IClientProvider>();
        var policies = new Mock<IRegistrationPolicyProvider>();
        var bus = new Mock<IEventBus>();
        var policy = new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "lab", IsEnabled = true, AutoApproval = true };
        policies.Setup(p => p.FindByPinAsync("PIN", It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        clients
            .Setup(p => p.InsertClientAsync(WorkspaceId, policy.Id, "sensor", It.IsAny<byte[]>(), ClientStatus.Registered, 25, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50025, "The workspace has reached its client limit."));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Clients:MaxPerWorkspace"] = "25" })
            .Build();
        using var metrics = new ManagementMetrics();
        var service = new ClientRegistrationService(clients.Object, policies.Object, bus.Object, NullLogger<ClientRegistrationService>.Instance, metrics, configuration);
        var controller = new ClientRegistrationsController(service, NullLogger<ClientRegistrationsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = (await controller.Initiate(new ClientRegistrationRequest("PIN", "sensor"), CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status409Conflict).Should().Be("WORKSPACE_CLIENT_LIMIT_REACHED");
        ProblemAssert.Detail(result).Should().Contain("25");
        bus.Verify(b => b.PublishAsync(It.IsAny<ClientRegistrationInitiatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
