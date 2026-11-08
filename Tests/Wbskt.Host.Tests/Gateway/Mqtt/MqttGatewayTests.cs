using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Gateway.MQTT.Host.Services;
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Gateway.Mqtt;

/// <summary>
/// The MQTT gateway against a real MQTTnet client over loopback TCP: login, the topic rules, the
/// reason codes a client sees, and that its sessions reach Gateway.Core like the WebSocket gateway's.
/// </summary>
public sealed class MqttGatewayTests : IAsyncLifetime
{
    private const string Message = """{"type":"temperature","payload":{"c":21}}""";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private static readonly Guid ClientRefId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IClientAccessGate> _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);
    private readonly JwtSigningKeys _keys = new(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem(), [], false, NullLogger.Instance);
    private readonly List<IMqttClient> _clients = [];
    private MqttGateway _gateway = null!;

    public async Task InitializeAsync()
    {
        var metrics = new GatewayMetrics(GatewayTransport.Mqtt);
        var dispatcher = new InboundMessageDispatcher([], new ClientMessageHandler(_bus.Object), metrics, NullLogger<InboundMessageDispatcher>.Instance);
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(x => x.ApplicationStopping).Returns(_stopping.Token);
        var sessions = new ClientSessions(_connections, _gate.Object, _bus.Object, new BusInstanceId("gateway-mqtt"), metrics, dispatcher,
            Options.Create(new MessageBudgetOptions()), lifetime.Object, NullLogger<ClientSessions>.Instance, NullLogger<ClientInbox>.Instance);
        var trust = JwtTrust.Local(JwtIssuers.Management, JwtAudiences.Socket, _keys.PublishedKeys);

        _gateway = new MqttGateway(sessions, trust, Options.Create(new MqttGatewayOptions { Port = 0 }), lifetime.Object,
            new MqttNetLoggerBridge(NullLogger<MqttNetLoggerBridge>.Instance), NullLogger<MqttGateway>.Instance);
        await _gateway.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _stopping.CancelAsync();
        await _gateway.StopAsync(CancellationToken.None);
        _keys.Dispose();
    }

    [Fact]
    public async Task A_client_with_its_token_connects_and_its_messages_reach_the_bus()
    {
        var client = await ConnectAsync();

        await client.PublishStringAsync(MqttBinding.UpTopic(ClientRefId), Message);

        await WaitUntilAsync(() => Published<ClientMessageReceivedEvent>().Any(e => e.ClientRefId == ClientRefId && e.Type == "temperature"));
        Assert.Contains(Published<ClientConnectedEvent>(), e => e.ClientRefId == ClientRefId && e.ClientId == 42 && e.WorkspaceId == 7);
    }

    [Fact]
    public async Task A_message_to_the_client_arrives_on_its_down_topic_without_it_subscribing()
    {
        var received = new TaskCompletionSource<MqttApplicationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = await ConnectAsync(onMessage: m => received.TrySetResult(m));
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);

        await _connections.GetConnection(ClientRefId)!.SendAsync(new ProtocolMessage("cmd.relay", new { on = true }), CancellationToken.None);

        var message = await received.Task.WaitAsync(Patience);
        Assert.Equal(MqttBinding.DownTopic(ClientRefId), message.Topic);
        Assert.Contains("\"cmd.relay\"", message.ConvertPayloadToString());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task A_send_after_the_client_left_fails_instead_of_vanishing()
    {
        var client = await ConnectAsync();
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);
        var connection = _connections.GetConnection(ClientRefId)!;

        await client.DisconnectAsync();
        await WaitUntilAsync(() => !connection.IsOpen);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(new ProtocolMessage("cmd.relay", new { on = true }), CancellationToken.None));
    }

    [Fact]
    public async Task A_token_that_does_not_validate_is_refused_as_a_bad_password()
    {
        var result = await ConnectExpectingRefusalAsync(token: "not-a-token");

        Assert.Equal(MqttClientConnectResultCode.BadUserNameOrPassword, result);
    }

    [Fact]
    public async Task A_revoked_client_is_refused_as_not_authorized()
    {
        _gate.Setup(x => x.IsRefusedAsync(ClientRefId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await ConnectExpectingRefusalAsync();

        Assert.Equal(MqttClientConnectResultCode.NotAuthorized, result);
    }

    [Fact]
    public async Task A_client_id_that_is_not_the_tokens_client_is_refused()
    {
        var result = await ConnectExpectingRefusalAsync(clientId: Guid.NewGuid().ToString());

        Assert.Equal(MqttClientConnectResultCode.ClientIdentifierNotValid, result);
    }

    [Fact]
    public async Task A_publish_to_another_topic_is_refused_and_reaches_nobody()
    {
        var client = await ConnectAsync();

        var result = await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(MqttBinding.UpTopic(Guid.NewGuid()))
            .WithPayload(Message)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());

        Assert.Equal(MqttClientPublishReasonCode.NotAuthorized, result.ReasonCode);
        Assert.Empty(Published<ClientMessageReceivedEvent>());
    }

    [Fact]
    public async Task A_subscription_to_another_clients_topic_is_refused()
    {
        var client = await ConnectAsync();

        var result = await client.SubscribeAsync(MqttBinding.DownTopic(Guid.NewGuid()));

        Assert.Equal(MqttClientSubscribeResultCode.NotAuthorized, Assert.Single(result.Items).ResultCode);
    }

    [Fact]
    public async Task A_client_closed_for_revocation_receives_not_authorized_and_is_announced_offline()
    {
        var disconnected = new TaskCompletionSource<MqttClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = await ConnectAsync();
        client.DisconnectedAsync += args =>
        {
            disconnected.TrySetResult(args);
            return Task.CompletedTask;
        };
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);

        await _connections.GetConnection(ClientRefId)!.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");

        Assert.Equal(MqttClientDisconnectReason.NotAuthorized, (await disconnected.Task.WaitAsync(Patience)).Reason);
        await WaitUntilAsync(() => Published<ClientDisconnectedEvent>().Any(e => e.ClientRefId == ClientRefId && e.Reason == "Client deleted."));
    }

    [Fact]
    public async Task A_second_connection_for_the_client_takes_over_without_announcing_it_offline()
    {
        var takenOver = new TaskCompletionSource<MqttClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = await ConnectAsync();
        old.DisconnectedAsync += args =>
        {
            takenOver.TrySetResult(args);
            return Task.CompletedTask;
        };
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);
        var first = _connections.GetConnection(ClientRefId);

        var fresh = await ConnectAsync();

        Assert.Equal(MqttClientDisconnectReason.SessionTakenOver, (await takenOver.Task.WaitAsync(Patience)).Reason);
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is { } current && !ReferenceEquals(current, first));
        await Task.Delay(200);
        Assert.True(fresh.IsConnected);
        Assert.Empty(Published<ClientDisconnectedEvent>());
    }

    [Fact]
    public async Task A_packet_over_the_limit_ends_the_session_as_too_big()
    {
        var client = await ConnectAsync();
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);

        try
        {
            await client.PublishStringAsync(MqttBinding.UpTopic(ClientRefId), new string('x', ClientProtocol.MaxMessageBytes + 4096));
        }
        catch (Exception)
        {
            // The server may cut the connection before the client finishes writing.
        }

        await WaitUntilAsync(() => Published<ClientDisconnectedEvent>().Any(e => e.Reason == DisconnectReason.MessageTooBig.Describe()));
    }

    [Fact]
    public async Task A_connection_claiming_a_huge_packet_is_dropped_before_it_authenticates()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _gateway.Listener.Port);
        var stream = tcp.GetStream();

        // CONNECT whose remaining length claims 2 MB and never sends it.
        await stream.WriteAsync(new byte[] { 0x10, 0x80, 0x80, 0x80, 0x01 });

        var read = await stream.ReadAsync(new byte[16]).AsTask().WaitAsync(Patience);
        Assert.Equal(0, read);
    }

    [Fact]
    public async Task A_client_that_disconnects_to_update_is_announced_as_updating()
    {
        var client = await ConnectAsync();
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);

        await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithUserProperty(MqttBinding.CloseReasonProperty, Encoding.UTF8.GetBytes(MqttBinding.UpdatingReason)).Build());

        await WaitUntilAsync(() => Published<ClientDisconnectedEvent>().Any(e => e.ClientRefId == ClientRefId && e.IsUpdating));
    }

    [Fact]
    public async Task A_gateway_that_stops_tells_its_clients_it_is_shutting_down()
    {
        var disconnected = new TaskCompletionSource<MqttClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = await ConnectAsync();
        client.DisconnectedAsync += args =>
        {
            disconnected.TrySetResult(args);
            return Task.CompletedTask;
        };
        await WaitUntilAsync(() => _connections.GetConnection(ClientRefId) is not null);

        await _stopping.CancelAsync();
        await _gateway.StopAsync(CancellationToken.None);

        Assert.Equal(MqttClientDisconnectReason.ServerShuttingDown, (await disconnected.Task.WaitAsync(Patience)).Reason);
        Assert.Contains(Published<ClientDisconnectedEvent>(), e => e.ClientRefId == ClientRefId && e.Reason == DisconnectReason.ServerStopping.Describe());
    }

    [Fact]
    public async Task A_311_client_connects_too()
    {
        var client = await ConnectAsync(version: MqttProtocolVersion.V311);

        await client.PublishStringAsync(MqttBinding.UpTopic(ClientRefId), Message);

        await WaitUntilAsync(() => Published<ClientMessageReceivedEvent>().Any(e => e.ClientRefId == ClientRefId));
    }

    private string Token() =>
        new JwtService(_keys, JwtIssuers.Management).GenerateToken(ClientClaims.For(ClientRefId, "sensor", Guid.NewGuid(), 42, 7), JwtAudiences.Socket, TimeSpan.FromMinutes(15));

    private MqttClientOptions ClientOptions(string? token, string? clientId, MqttProtocolVersion version) => new MqttClientOptionsBuilder()
        .WithTcpServer("127.0.0.1", _gateway.Listener.Port)
        .WithClientId(clientId ?? ClientRefId.ToString())
        .WithCredentials(ClientRefId.ToString(), token ?? Token())
        .WithProtocolVersion(version)
        .WithCleanSession()
        .Build();

    private async Task<IMqttClient> ConnectAsync(Action<MqttApplicationMessage>? onMessage = null, MqttProtocolVersion version = MqttProtocolVersion.V500)
    {
        var client = new MqttClientFactory().CreateMqttClient();
        _clients.Add(client);
        if (onMessage is not null)
        {
            client.ApplicationMessageReceivedAsync += args =>
            {
                onMessage(args.ApplicationMessage);
                return Task.CompletedTask;
            };
        }

        await client.ConnectAsync(ClientOptions(null, null, version));
        return client;
    }

    private async Task<MqttClientConnectResultCode> ConnectExpectingRefusalAsync(string? token = null, string? clientId = null)
    {
        var client = new MqttClientFactory().CreateMqttClient();
        _clients.Add(client);
        var result = await client.ConnectAsync(ClientOptions(token, clientId, MqttProtocolVersion.V500));
        Assert.False(client.IsConnected);
        return result.ResultCode;
    }

    private IEnumerable<T> Published<T>() => _bus.Invocations
        .Where(i => i.Method.Name == nameof(IEventBus.PublishAsync))
        .Select(i => i.Arguments[0])
        .OfType<T>()
        .ToList();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(20);
        }
    }
}
