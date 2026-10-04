using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Handlers;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

/// <summary>
/// Every socket host receives every command. A missing connection must be reported exactly once (by
/// the host the sender saw holding it), and an expired command must never reach the device.
/// </summary>
public sealed class ClientPayloadHandlerTests
{
    private const string ThisHost = "socket-a";
    private static readonly Guid ClientRefId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CommandId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly List<string> _sentFrames = [];

    private ClientPayloadHandler CreateHandler()
    {
        return new ClientPayloadHandler(_connections.Object, NullLogger<ClientPayloadHandler>.Instance, _bus.Object, new BusInstanceId(ThisHost), new FixedTime(Now));
    }

    private void Connected()
    {
        var socket = new Mock<WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((bytes, _, _, _) => _sentFrames.Add(Encoding.UTF8.GetString(bytes)))
            .Returns(Task.CompletedTask);
        _connections.Setup(x => x.GetConnection(ClientRefId))
            .Returns(new ClientConnection { Socket = socket.Object, ClientId = 42, WorkspaceId = 7 });
    }

    private static ConsumeContext<ClientCommandEvent> Context(string? targetHostId = ThisHost, DateTime? expiresAtUtc = null)
    {
        var ctx = new Mock<ConsumeContext<ClientCommandEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new ClientCommandEvent(ClientRefId, 42, 7, "pump.start", "{}", CommandId, targetHostId, expiresAtUtc));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task The_target_host_reports_a_missing_connection_as_a_failed_command()
    {
        await CreateHandler().Consume(Context(targetHostId: ThisHost));

        _bus.Verify(x => x.PublishAsync(
            It.Is<ClientCommandFailedEvent>(e => e.CommandId == CommandId && e.Reason == ClientPayloadHandler.NotConnectedReason && e.Type == "pump.start"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("socket-b")]
    [InlineData(null)]
    public async Task Any_other_host_stays_quiet_about_a_connection_it_never_had(string? targetHostId)
    {
        await CreateHandler().Consume(Context(targetHostId));

        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_expired_command_is_reported_and_never_sent()
    {
        Connected();

        await CreateHandler().Consume(Context(expiresAtUtc: Now.UtcDateTime.AddSeconds(-1)));

        _sentFrames.Should().BeEmpty();
        _bus.Verify(x => x.PublishAsync(
            It.Is<ClientCommandFailedEvent>(e => e.CommandId == CommandId && e.Reason == ClientPayloadHandler.ExpiredReason),
            It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandDeliveredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_live_command_is_sent_with_its_expiry_and_reported_delivered()
    {
        Connected();
        var expiresAt = Now.UtcDateTime.AddMinutes(1);

        await CreateHandler().Consume(Context(expiresAtUtc: expiresAt));

        var frame = JsonDocument.Parse(_sentFrames.Should().ContainSingle().Subject).RootElement;
        frame.GetProperty("commandId").GetString().Should().Be(CommandId.ToString());
        frame.GetProperty("expiresAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(expiresAt, TimeSpan.Zero));
        _bus.Verify(x => x.PublishAsync(It.Is<ClientCommandDeliveredEvent>(e => e.CommandId == CommandId), It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_command_without_an_expiry_omits_the_field()
    {
        Connected();

        await CreateHandler().Consume(Context());

        JsonDocument.Parse(_sentFrames.Single()).RootElement.TryGetProperty("expiresAt", out _).Should().BeFalse();
    }
}

file sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
