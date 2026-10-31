using Wbskt.Protocol;
using Wbskt.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics.Metrics;
using Wbskt.Management.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Devices;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// For a command or a ping the publish is the action, so the outcomes a caller can act on (queued for
/// an offline device, a full queue, broker down, a bad request) come back as results, without a
/// controller in the way.
/// </summary>
public sealed class ClientCommandServiceTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid ClientRef = Guid.NewGuid();

    private readonly Mock<IClientQueryService> _clients = new();
    private readonly Mock<IClientCommandTargetProvider> _targets = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly InMemoryQueuedCommandStore _queue = new();
    private readonly CommandQueueOptions _options = new();

    private readonly ManagementMetrics _metrics = new();

    private ClientCommandService Service =>
        new(_clients.Object, TestDeviceCommands.Sender(_targets.Object, _bus.Object, _queue, options: _options), _queue, _bus.Object, Options.Create(_options), NullLogger<ClientCommandService>.Instance, _metrics);

    private void InWorkspace(int clientId) =>
        _clients.Setup(c => c.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(clientId));

    [Fact]
    public async Task Every_command_is_counted_by_its_outcome()
    {
        _targets.Setup(t => t.FindAsync(ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientCommandTarget(42, WorkspaceId, false, null, ClientStatus.Revoked));
        var outcomes = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument, _metrics.Commands))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => outcomes.Add(tags[0].Value as string));
        listener.Start();

        await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}"));

        outcomes.Should().Equal("DEVICE_INACTIVE");
    }

    [Fact]
    public async Task A_command_to_an_offline_device_is_queued_with_its_expiry()
    {
        _targets.Setup(t => t.FindAsync(ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientCommandTarget(42, WorkspaceId, false, null));
        var expiresAt = new DateTimeOffset(DateTime.UtcNow.AddHours(1).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}", expiresAt));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ClientCommandStatus.Queued);
        result.Value.ExpiresAt.Should().Be(expiresAt);
        _queue.Rows.Should().ContainSingle().Which.CommandId.Should().Be(result.Value.CommandId);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_command_to_an_offline_device_with_a_full_queue_is_a_conflict()
    {
        _targets.Setup(t => t.FindAsync(ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientCommandTarget(42, WorkspaceId, false, null));
        _options.MaxPerClient = 0;

        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}"));

        result.Error.Code.Should().Be("COMMAND_QUEUE_FULL");
        _queue.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_to_an_offline_device_that_is_not_registered_is_a_conflict()
    {
        _targets.Setup(t => t.FindAsync(ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientCommandTarget(42, WorkspaceId, false, null, ClientStatus.Pending));

        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest("reboot", "{}"));

        result.Error.Code.Should().Be("DEVICE_INACTIVE");
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_waiting_commands_are_listed_oldest_first()
    {
        InWorkspace(42);
        var later = DateTime.UtcNow.AddHours(1);
        await _queue.EnqueueAsync(new QueuedCommand(Guid.NewGuid(), 42, ClientRef, WorkspaceId, "first", "{}", later), 10, CancellationToken.None);
        await _queue.EnqueueAsync(new QueuedCommand(Guid.NewGuid(), 42, ClientRef, WorkspaceId, "second", "{}", later), 10, CancellationToken.None);
        await _queue.EnqueueAsync(new QueuedCommand(Guid.NewGuid(), 43, Guid.NewGuid(), WorkspaceId, "other device", "{}", later), 10, CancellationToken.None);

        var result = await Service.GetQueuedAsync(WorkspaceId, ClientRef);

        result.Value.Select(c => c.Type).Should().Equal("first", "second");
        result.Value[0].ExpiresAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Cancelling_a_waiting_command_takes_it_out_and_announces_it()
    {
        InWorkspace(42);
        var commandId = Guid.NewGuid();
        await _queue.EnqueueAsync(new QueuedCommand(commandId, 42, ClientRef, WorkspaceId, "reboot", "{}", DateTime.UtcNow.AddHours(1)), 10, CancellationToken.None);

        var result = await Service.CancelAsync(WorkspaceId, ClientRef, commandId);

        result.IsSuccess.Should().BeTrue();
        _queue.Rows.Should().BeEmpty();
        _bus.Verify(b => b.PublishAsync(It.Is<ClientCommandCancelledEvent>(e => e.CommandId == commandId && e.ClientId == 42), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelling_a_command_that_is_not_waiting_is_not_found()
    {
        InWorkspace(42);

        var result = await Service.CancelAsync(WorkspaceId, ClientRef, Guid.NewGuid());

        result.Error.Code.Should().Be("COMMAND_NOT_QUEUED");
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Another_devices_command_cannot_be_cancelled_through_this_one()
    {
        InWorkspace(42);
        var commandId = Guid.NewGuid();
        await _queue.EnqueueAsync(new QueuedCommand(commandId, 43, Guid.NewGuid(), WorkspaceId, "reboot", "{}", DateTime.UtcNow.AddHours(1)), 10, CancellationToken.None);

        var result = await Service.CancelAsync(WorkspaceId, ClientRef, commandId);

        result.Error.Code.Should().Be("COMMAND_NOT_QUEUED");
        _queue.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_command_with_the_broker_down_is_unavailable()
    {
        _targets.Setup(t => t.FindAsync(ClientRef, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientCommandTarget(42, WorkspaceId, true, "socket-a"));
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
        var result = await Service.SendAsync(WorkspaceId, ClientRef, new ClientCommandRequest(MessageTypes.StateReport, "{}"));

        result.Error.Code.Should().Be("COMMAND_TYPE_RESERVED");
        _targets.VerifyNoOtherCalls();
    }

    [Fact]
    public void Unavailable_maps_to_503_with_retry_after()
    {
        var result = ApiErrorResults.From(ClientCommandService.BrokerUnavailable, NullLogger.Instance);
        var context = new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        result.OnFormatting(context);

        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be(ApiErrorResults.RetryAfterSeconds);
        ProblemAssert.Is(result, 503).Should().Be(ClientCommandService.BrokerUnavailable.Code);
        ProblemAssert.Detail(result).Should().Be(ClientCommandService.BrokerUnavailable.Message);
    }
}
