using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Devices;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Primitives;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

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
    private static readonly Guid ClientRefId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string HostId = "socket-a";
    private static readonly Error Foreign = Error.NotFound("CLIENT_NOT_FOUND", "Client not found.");

    // The workspace and the caller's permission are settled by WorkspacePermissionFilter before the
    // action runs (see WorkspacePermissionFilterTests), so the actions are handed the resolved ID.
    private readonly Mock<IClientCommandTargetProvider> _targets = new();

    private (ClientsController Controller, Mock<IClientQueryService> ClientService, Mock<IEventBus> Bus, Mock<IEventLogService> EventLogService) CreateController()
    {
        var clientService = new Mock<IClientQueryService>();
        var bus = new Mock<IEventBus>();
        var eventLogService = new Mock<IEventLogService>();

        var controller = new ClientsController(clientService.Object, Mock.Of<IClientLifecycleService>(), new ClientCommandService(clientService.Object, new DeviceCommandSender(_targets.Object, bus.Object, NullLogger<DeviceCommandSender>.Instance), bus.Object, NullLogger<ClientCommandService>.Instance, new Wbskt.Management.Host.Telemetry.ManagementMetrics()), Mock.Of<IRegistrationPolicyService>(), eventLogService.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (controller, clientService, bus, eventLogService);
    }

    private void SetupTarget(ClientCommandTarget? target)
    {
        _targets.Setup(x => x.FindAsync(ClientRefId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
    }

    private static void SetupOwnership(Mock<IClientQueryService> clientService, Result<int> outcome)
    {
        clientService
            .Setup(x => x.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
    }

    [Fact]
    public async Task SendCommand_does_not_publish_for_a_client_in_another_workspace()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupTarget(new ClientCommandTarget(ClientId, WorkspaceId + 1, true, HostId));

        var result = (await controller.SendCommand(WorkspaceId, ClientRefId, new ClientCommandRequest("reboot", "{}"), CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status404NotFound);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_publishes_with_the_resolved_client_id_when_owned()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupTarget(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId));

        var result = (await controller.SendCommand(WorkspaceId, ClientRefId, new ClientCommandRequest("reboot", "{}"), CancellationToken.None)).Result;

        Assert.IsType<AcceptedResult>(result);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientCommandEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == ClientId && e.WorkspaceId == WorkspaceId
                && e.TargetHostId == HostId && e.ExpiresAtUtc == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCommand_to_an_offline_device_is_a_409_and_publishes_nothing()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupTarget(new ClientCommandTarget(ClientId, WorkspaceId, false, null));

        var result = (await controller.SendCommand(WorkspaceId, ClientRefId, new ClientCommandRequest("reboot", "{}"), CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status409Conflict).Should().Be("DEVICE_OFFLINE");
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_carries_expiresAt_to_the_socket_host()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupTarget(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(2);

        var result = (await controller.SendCommand(WorkspaceId, ClientRefId, new ClientCommandRequest("reboot", "{}", expiresAt), CancellationToken.None)).Result;

        Assert.IsType<AcceptedResult>(result);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientCommandEvent>(e => e.ExpiresAtUtc == expiresAt.UtcDateTime),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25 * 60)]
    public async Task SendCommand_rejects_an_expiresAt_in_the_past_or_over_a_day_away(int minutesAhead)
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupTarget(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId));

        var request = new ClientCommandRequest("reboot", "{}", DateTimeOffset.UtcNow.AddMinutes(minutesAhead));
        var result = (await controller.SendCommand(WorkspaceId, ClientRefId, request, CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status400BadRequest).Should().Be("COMMAND_EXPIRY_INVALID");
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ping_does_not_publish_for_a_client_in_another_workspace()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupOwnership(clientService, Result<int>.Failure(Foreign));

        var result = await controller.Ping(WorkspaceId, ClientRefId, CancellationToken.None);

        ProblemAssert.Is(result, StatusCodes.Status404NotFound);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPingEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ping_publishes_with_the_resolved_client_id_when_owned()
    {
        var (controller, clientService, bus, _) = CreateController();
        SetupOwnership(clientService, Result<int>.Success(ClientId));

        var result = await controller.Ping(WorkspaceId, ClientRefId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPingEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == ClientId && e.WorkspaceId == WorkspaceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetComms_reports_a_client_in_another_workspace_as_not_found_rather_than_empty()
    {
        var (controller, clientService, _, eventLogService) = CreateController();
        SetupOwnership(clientService, Result<int>.Failure(Foreign));

        var result = await controller.GetComms(WorkspaceId, ClientRefId, direction: null, new PageRequest(), cancellationToken: CancellationToken.None);

        ProblemAssert.Is(result.Result, StatusCodes.Status404NotFound);
        eventLogService.Verify(x => x.GetClientCommsAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
