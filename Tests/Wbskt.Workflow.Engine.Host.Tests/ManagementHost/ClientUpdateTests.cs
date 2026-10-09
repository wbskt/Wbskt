using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// <c>PATCH /clients/{clientRef}</c> changes a client's name, status or both, and the old
/// <c>/name</c> and <c>/status</c> routes do exactly what it does for one field.
/// </summary>
public sealed class ClientUpdateTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid ClientRef = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (ClientsController Controller, Mock<IClientLifecycleService> Clients) CreateController()
    {
        var clients = new Mock<IClientLifecycleService>();
        var queries = new Mock<IClientQueryService>();
        clients.Setup(x => x.UpdateStatusAsync(WorkspaceId, ClientRef, It.IsAny<ClientStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        clients.Setup(x => x.RenameAsync(WorkspaceId, ClientRef, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var controller = new ClientsController(
            queries.Object,
            clients.Object,
            new ClientCommandService(queries.Object, Mock.Of<IEventBus>(), NullLogger<ClientCommandService>.Instance),
            Mock.Of<IRegistrationPolicyService>(),
            Mock.Of<IEventLogService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, clients);
    }

    [Fact]
    public async Task Both_fields_change_in_one_request()
    {
        var (controller, clients) = CreateController();

        var result = await controller.Update(WorkspaceId, ClientRef, new UpdateClientRequest("  porch  ", ClientStatus.Registered), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        clients.Verify(x => x.UpdateStatusAsync(WorkspaceId, ClientRef, ClientStatus.Registered, It.IsAny<CancellationToken>()), Times.Once);
        clients.Verify(x => x.RenameAsync(WorkspaceId, ClientRef, "porch", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_request_naming_no_field_is_a_bad_request()
    {
        var (controller, clients) = CreateController();

        var result = await controller.Update(WorkspaceId, ClientRef, new UpdateClientRequest(null, null), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CLIENT_UPDATE_EMPTY", Assert.IsType<Error>(bad.Value).Code);
        clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_changes_nothing_not_even_the_status()
    {
        var (controller, clients) = CreateController();

        var result = await controller.Update(WorkspaceId, ClientRef, new UpdateClientRequest("   ", ClientStatus.Revoked), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CLIENT_NAME_INVALID", Assert.IsType<Error>(bad.Value).Code);
        clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_refused_status_leaves_the_name_alone()
    {
        var (controller, clients) = CreateController();
        clients.Setup(x => x.UpdateStatusAsync(WorkspaceId, ClientRef, ClientStatus.Registered, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Conflict("POLICY_FULL", "The policy is full.")));

        var result = await controller.Update(WorkspaceId, ClientRef, new UpdateClientRequest("porch", ClientStatus.Registered), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        clients.Verify(x => x.RenameAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_old_routes_change_one_field_each()
    {
        var (controller, clients) = CreateController();

        Assert.IsType<NoContentResult>(await controller.UpdateStatus(WorkspaceId, ClientRef, new UpdateClientStatusRequest(ClientStatus.Revoked), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Rename(WorkspaceId, ClientRef, new UpdateClientNameRequest("garage"), CancellationToken.None));

        clients.Verify(x => x.UpdateStatusAsync(WorkspaceId, ClientRef, ClientStatus.Revoked, It.IsAny<CancellationToken>()), Times.Once);
        clients.Verify(x => x.RenameAsync(WorkspaceId, ClientRef, "garage", It.IsAny<CancellationToken>()), Times.Once);
        clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_old_rename_route_still_rejects_a_blank_name()
    {
        var (controller, _) = CreateController();

        var result = await controller.Rename(WorkspaceId, ClientRef, new UpdateClientNameRequest(" "), CancellationToken.None);

        Assert.Equal("CLIENT_NAME_INVALID", Assert.IsType<Error>(Assert.IsType<BadRequestObjectResult>(result).Value).Code);
    }
}
