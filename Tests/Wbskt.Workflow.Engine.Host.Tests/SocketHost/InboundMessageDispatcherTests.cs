using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Services;
using Wbskt.Socket.Host.Services.Inbound;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class InboundMessageDispatcherTests
{
    private static readonly Guid ClientRefId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid CommandId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private readonly RecordingEventBus _bus = new();
    private readonly ClientConnection _connection = new() { Socket = Mock.Of<WebSocket>(), ClientId = 1, WorkspaceId = 2 };
    private readonly InboundMessageDispatcher _dispatcher;

    public InboundMessageDispatcherTests()
    {
        IInboundMessageHandler[] handlers =
        [
            new PongHandler(_bus, NullLogger<PongHandler>.Instance),
            new AckHandler(_bus, NullLogger<AckHandler>.Instance)
        ];
        _dispatcher = new InboundMessageDispatcher(handlers, new DeviceMessageHandler(_bus), new SocketMetrics(), NullLogger<InboundMessageDispatcher>.Instance);
    }

    private Task Dispatch(string json) => _dispatcher.DispatchAsync(new InboundContext(ClientRefId, _connection), json, CancellationToken.None);

    [Fact]
    public async Task A_device_message_is_published_as_the_devices_own()
    {
        await Dispatch("""{"type":"temp.reading","payload":{"c":21.5}}""");

        var received = _bus.Published.Should().ContainSingle().Which.Should().BeOfType<ClientMessageReceivedEvent>().Subject;
        received.Type.Should().Be("temp.reading");
        received.Payload.Should().Be("""{"c":21.5}""");
        received.ClientId.Should().Be(1);
        received.WorkspaceId.Should().Be(2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"payload":{}}""")]
    [InlineData("""{"type":"  ","payload":{}}""")]
    public async Task Invalid_json_or_a_missing_type_is_dropped(string json)
    {
        await Dispatch(json);

        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task An_over_long_type_is_dropped()
    {
        await Dispatch($$$"""{"type":"{{{new string('t', InboundMessageDispatcher.MaxTypeLength + 1)}}}","payload":{}}""");

        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_system_message_is_ignored_and_never_reaches_the_device_log()
    {
        await Dispatch("""{"type":"sys.whatever","payload":{}}""");

        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task An_ack_is_published_as_an_acked_command()
    {
        await Dispatch($$$"""{"type":"sys.ack","payload":{"commandId":"{{{CommandId}}}"}}""");

        _bus.Published.Should().ContainSingle().Which.Should().BeOfType<ClientCommandAckedEvent>()
            .Which.CommandId.Should().Be(CommandId);
    }

    [Fact]
    public async Task A_refused_ack_is_published_as_a_failed_command()
    {
        await Dispatch($$$"""{"type":"sys.ack","payload":{"commandId":"{{{CommandId}}}","type":"pump.start","refused":"expired"}}""");

        var failed = _bus.Published.Should().ContainSingle().Which.Should().BeOfType<ClientCommandFailedEvent>().Subject;
        failed.CommandId.Should().Be(CommandId);
        failed.Type.Should().Be("pump.start");
    }

    [Fact]
    public async Task An_ack_without_a_command_id_publishes_nothing()
    {
        await Dispatch("""{"type":"sys.ack","payload":{}}""");

        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pong_echoing_the_ping_time_is_published_as_a_latency_reading()
    {
        var pingedAt = DateTime.UtcNow.AddMilliseconds(-250);

        await Dispatch($$$"""{"type":"sys.pong","payload":{"originalTimestamp":"{{{pingedAt:O}}}"}}""");

        _bus.Published.Should().ContainSingle().Which.Should().BeOfType<ClientLatencyMeasuredEvent>()
            .Which.RoundTripMs.Should().BeGreaterThanOrEqualTo(250);
    }

    [Fact]
    public async Task A_pong_with_no_ping_to_match_publishes_nothing()
    {
        await Dispatch("""{"type":"sys.pong","payload":{}}""");

        _bus.Published.Should().BeEmpty();
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}

public sealed class WebSocketMessageReaderTests
{
    [Fact]
    public async Task A_text_message_split_across_frames_is_read_whole()
    {
        var socket = Socket(("{\"type\":", WebSocketMessageType.Text, false), ("\"a\"}", WebSocketMessageType.Text, true));
        using var reader = new WebSocketMessageReader(socket, 1024);

        var read = await reader.ReadAsync(CancellationToken.None);

        read.Should().Be(new MessageRead(FrameKind.Text, "{\"type\":\"a\"}"));
    }

    [Fact]
    public async Task A_message_past_the_limit_is_reported_as_too_big()
    {
        var socket = Socket((new string('x', 600), WebSocketMessageType.Text, false), (new string('x', 600), WebSocketMessageType.Text, true));
        using var reader = new WebSocketMessageReader(socket, 1024);

        (await reader.ReadAsync(CancellationToken.None)).Kind.Should().Be(FrameKind.TooBig);
    }

    [Fact]
    public async Task Binary_and_close_frames_are_reported_by_kind()
    {
        var socket = Socket(("bin", WebSocketMessageType.Binary, true), ("", WebSocketMessageType.Close, true));
        using var reader = new WebSocketMessageReader(socket, 1024);

        (await reader.ReadAsync(CancellationToken.None)).Kind.Should().Be(FrameKind.Binary);
        (await reader.ReadAsync(CancellationToken.None)).Kind.Should().Be(FrameKind.Closed);
    }

    [Fact]
    public async Task Each_read_starts_a_new_message()
    {
        var socket = Socket(("first", WebSocketMessageType.Text, true), ("second", WebSocketMessageType.Text, true));
        using var reader = new WebSocketMessageReader(socket, 1024);

        (await reader.ReadAsync(CancellationToken.None)).Text.Should().Be("first");
        (await reader.ReadAsync(CancellationToken.None)).Text.Should().Be("second");
    }

    private static WebSocket Socket(params (string Text, WebSocketMessageType Type, bool End)[] frames)
    {
        var queue = new Queue<(string Text, WebSocketMessageType Type, bool End)>(frames);
        var socket = new Mock<WebSocket>();
        socket.Setup(x => x.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((ArraySegment<byte> buffer, CancellationToken _) =>
            {
                var frame = queue.Dequeue();
                var bytes = Encoding.UTF8.GetBytes(frame.Text);
                bytes.CopyTo(buffer.Array!, buffer.Offset);
                return Task.FromResult(new WebSocketReceiveResult(bytes.Length, frame.Type, frame.End));
            });
        return socket.Object;
    }
}
