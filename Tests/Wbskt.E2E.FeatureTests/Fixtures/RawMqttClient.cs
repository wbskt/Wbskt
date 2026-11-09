using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Formatter;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// A client on the MQTT gateway, as Docs/Client.Protocol.MQTT.md describes one: its RefId as client
/// id and user name, its access token as password, publishing on its up topic and reading its down
/// topic. Like <see cref="RawClientSocket"/>, it is not an SDK, so a scenario can send what an SDK
/// never would.
/// </summary>
public sealed class RawMqttClient : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly Channel<JsonElement> _received = Channel.CreateUnbounded<JsonElement>();
    private readonly TaskCompletionSource<MqttClientDisconnectReason> _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RawMqttClient(IMqttClient client, Guid clientRefId)
    {
        _client = client;
        ClientRefId = clientRefId;
        _client.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == MqttBinding.DownTopic(clientRefId))
            {
                _received.Writer.TryWrite(JsonSerializer.Deserialize<JsonElement>(args.ApplicationMessage.ConvertPayloadToString()));
            }

            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += args =>
        {
            // A refused CONNECT raises this too; only a connection that was up has a reason to report.
            if (args.ClientWasConnected)
            {
                _disconnected.TrySetResult(args.Reason);
            }

            return Task.CompletedTask;
        };
    }

    public Guid ClientRefId { get; }

    public bool IsConnected => _client.IsConnected;

    /// <summary>Connects, or throws with the CONNACK code when the gateway refuses.</summary>
    public static async Task<RawMqttClient> ConnectAsync(Guid clientRefId, string token, MqttProtocolVersion version = MqttProtocolVersion.V500)
    {
        var (code, client) = await TryConnectAsync(clientRefId, token, version);
        return client ?? throw new InvalidOperationException($"The MQTT gateway refused the connection with {code}.");
    }

    /// <summary>Connects and returns the CONNACK code, with the client when it is Success.</summary>
    public static async Task<(MqttClientConnectResultCode Code, RawMqttClient? Client)> TryConnectAsync(
        Guid clientRefId, string token, MqttProtocolVersion version = MqttProtocolVersion.V500)
    {
        var mqtt = new MqttClientFactory().CreateMqttClient();
        var client = new RawMqttClient(mqtt, clientRefId);
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(E2EConfig.MqttUrl.Host, E2EConfig.MqttUrl.Port)
            .WithClientId(clientRefId.ToString("D"))
            .WithCredentials(clientRefId.ToString("D"), token)
            .WithProtocolVersion(version)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(15))
            .Build();

        try
        {
            var result = await mqtt.ConnectAsync(options);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                return (result.ResultCode, client);
            }

            mqtt.Dispose();
            return (result.ResultCode, null);
        }
        catch
        {
            // Unreachable or timed out: nothing to hand back, so nothing left open.
            mqtt.Dispose();
            throw;
        }
    }

    /// <summary>Publishes <paramref name="text"/> on the client's up topic, verbatim.</summary>
    public Task PublishRawAsync(string text) =>
        _client.PublishStringAsync(MqttBinding.UpTopic(ClientRefId), text);

    /// <summary>Publishes a protocol message serialized as the SDKs serialize it.</summary>
    public Task PublishAsync(ProtocolMessage message) => PublishRawAsync(JsonSerializer.Serialize(message));

    /// <summary>The next message of <paramref name="type"/> on the down topic, skipping others (the gateway's pings).</summary>
    public async Task<JsonElement> NextOfAsync(string type, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            var message = await _received.Reader.ReadAsync(cts.Token);
            if (message.TryGetProperty("type", out var t) && t.GetString() == type)
            {
                return message;
            }
        }
    }

    /// <summary>The reason code of the DISCONNECT the gateway ended the connection with, once it has.</summary>
    public Task<MqttClientDisconnectReason> Disconnected => _disconnected.Task;

    /// <summary>Closes for a firmware update: an MQTT 5 DISCONNECT with the updating user property.</summary>
    public Task CloseForUpdateAsync() =>
        _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
            .WithUserProperty(MqttBinding.CloseReasonProperty, Encoding.UTF8.GetBytes(MqttBinding.UpdatingReason))
            .Build());

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync();
            }
            catch
            {
                // Gone already.
            }
        }

        _client.Dispose();
    }
}
