using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// The command and ping endpoints publish onto the bus, and the socket host dispatches on
/// <c>ClientRefId</c> alone — it has no workspace to check the event against. The membership check
/// in the controller is therefore the only thing standing between a caller and a client in someone
/// else's workspace, so it is covered here directly.
/// </summary>
public sealed class ClientsControllerScopingTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ClientRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Error Foreign = Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace.");

    private static (ClientsController Controller, Mock<IClientService> ClientService, Mock<IEventBus> Bus, Mock<IEventLogService> EventLogService) CreateController(PermissionSlug permission)
    {
        var authClient = new Mock<IAuthServiceClient>();
        authClient.Setup(x => x.ResolveWorkspaceAsync(WorkspaceRef, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(WorkspaceId));

        var clientService = new Mock<IClientService>();
        var bus = new Mock<IEventBus>();
        var eventLogService = new Mock<IEventLogService>();

        var controller = new ClientsController(
            clientService.Object,
            Mock.Of<IReferenceMapper>(),
            authClient.Object,
            bus.Object,
            Mock.Of<IReferenceMapper>(),
            Mock.Of<IRegistrationPolicyService>(),
            eventLogService.Object,
            NullLogger<ClientsController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (controller, clientService, bus, eventLogService);
    }

    private static void SetupOwnership(Mock<IClientService> clientService, Result<int> outcome)
    {
        clientService
            .Setup(x => x.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
    }

    [Fact]
    public async Task SendCommand_does_not_publish_for_a_client_in_another_workspace()
    {
        var (controller, clientService, bus, _) = CreateController(Permissions.ClientsCommand);
        SetupOwnership(clientService, Result<int>.Failure(Foreign));

        var result = await controller.SendCommand(WorkspaceRef, ClientRefId, new ClientCommandRequest("reboot", "{}"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_publishes_with_the_resolved_client_id_when_owned()
    {
        var (controller, clientService, bus, _) = CreateController(Permissions.ClientsCommand);
        SetupOwnership(clientService, Result<int>.Success(ClientId));

        var result = await controller.SendCommand(WorkspaceRef, ClientRefId, new ClientCommandRequest("reboot", "{}"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientCommandEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == ClientId && e.WorkspaceId == WorkspaceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ping_does_not_publish_for_a_client_in_another_workspace()
    {
        var (controller, clientService, bus, _) = CreateController(Permissions.ClientsPing);
        SetupOwnership(clientService, Result<int>.Failure(Foreign));

        var result = await controller.Ping(WorkspaceRef, ClientRefId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPingEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ping_publishes_with_the_resolved_client_id_when_owned()
    {
        var (controller, clientService, bus, _) = CreateController(Permissions.ClientsPing);
        SetupOwnership(clientService, Result<int>.Success(ClientId));

        var result = await controller.Ping(WorkspaceRef, ClientRefId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPingEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == ClientId && e.WorkspaceId == WorkspaceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetComms_reports_a_client_in_another_workspace_as_forbidden_rather_than_empty()
    {
        var (controller, clientService, _, eventLogService) = CreateController(Permissions.LogsRead);
        SetupOwnership(clientService, Result<int>.Failure(Foreign));

        var result = await controller.GetComms(WorkspaceRef, ClientRefId, direction: null, cancellationToken: CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        eventLogService.Verify(x => x.GetClientCommsAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
