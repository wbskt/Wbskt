using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// For a command or a ping the publish is the action, so the outcomes a caller can act on (device
/// offline, broker down, a bad request) come back as results, without a controller in the way.
/// </summary>
public sealed class ClientCommandServiceTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid ClientRef = Guid.NewGuid();

    private readonly Mock<IClientQueryService> _clients = new();
    private readonly Mock<IEventBus> _bus = new();

    private ClientCommandService Service => new(_clients.Object, _bus.Object, NullLogger<ClientCommandService>.Instance);

    [Fact]
    public async Task A_command_to_an_offline_device_is_a_conflict_and_sends_nothing()
    {
        _clients.Setup(c => c.ResolveCommandTargetAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClientCommandTarget>.Failure(Error.Conflict("DEVICE_OFFLINE", "offline")));

        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}"));

        result.Error.Code.Should().Be("DEVICE_OFFLINE");
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_command_with_the_broker_down_is_unavailable()
    {
        _clients.Setup(c => c.ResolveCommandTargetAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClientCommandTarget>.Success(new ClientCommandTarget(42, "socket-a")));
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}"));

        result.Error.Should().Be(ClientCommandService.BrokerUnavailable);
    }

    [Fact]
    public async Task A_ping_with_the_broker_down_is_unavailable()
    {
        _clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(42));
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientPingEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Service.PingAsync(WorkspaceId, ClientRef);

        result.Error.Should().Be(ClientCommandService.BrokerUnavailable);
    }

    [Fact]
    public async Task A_reserved_command_type_is_refused_before_the_device_is_looked_up()
    {
        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest(ReservedMessageTypes.StateReport, "{}"));

        result.Error.Code.Should().Be("COMMAND_TYPE_RESERVED");
        _clients.VerifyNoOtherCalls();
    }

    [Fact]
    public void Unavailable_maps_to_503_with_retry_after()
    {
        var result = ApiErrorResults.From(ClientCommandService.BrokerUnavailable, NullLogger.Instance);
        var context = new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        result.OnFormatting(context);

        result.StatusCode.Should().Be(503);
        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be(ApiErrorResults.RetryAfterSeconds);
        result.Value.Should().Be(ClientCommandService.BrokerUnavailable);
    }
}
