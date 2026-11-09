using System.Collections;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Gateway.MQTT.Host.Models;

namespace Wbskt.Gateway.MQTT.Host.Services;

/// <summary>
/// The MQTT transport: an embedded MQTTnet server that is not a broker. It wires the server's events:
/// CONNECT goes to <see cref="MqttConnectAuthenticator"/>, each client is held to its own two topics,
/// every PUBLISH goes to the client's session instead of being routed, and a disconnect is classified
/// by <see cref="MqttDisconnectClassifier"/>. Who may connect and what a session means (registering the client,
/// announcing it, budgeting and routing its messages, recording its end) is Gateway.Core's
/// <see cref="ClientSessions"/>, the same as for the WebSocket gateway.
/// </summary>
public sealed class MqttGateway : IHostedService
{
    // How long stopping waits for sessions to announce their end before the server goes.
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly ClientSessions _sessions;
    private readonly MqttConnectAuthenticator _authenticator;
    private readonly MqttGatewayOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MqttGateway> _logger;
    private readonly MqttServer _server;
    private readonly ConcurrentDictionary<MqttConnection, Task> _running = new();

    public MqttGateway(
        ClientSessions sessions,
        MqttConnectAuthenticator authenticator,
        IOptions<MqttGatewayOptions> options,
        IHostApplicationLifetime lifetime,
        MqttNetLoggerBridge mqttLogger,
        ILogger<MqttGateway> logger)
    {
        _sessions = sessions;
        _authenticator = authenticator;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;

        Listener = new GuardedTcpServerAdapter(_options.Port, _options.MaxPacketBytes);
        var serverOptions = new MqttServerOptionsBuilder()
            .WithPersistentSessions(false)
            .Build();
        _server = new MqttServerFactory().CreateMqttServer(serverOptions, [Listener], mqttLogger);
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
        _server.ClientConnectedAsync += StartSessionAsync;
        _server.ClientDisconnectedAsync += EndConnectionAsync;
        _server.InterceptingPublishAsync += ReceivePublishAsync;
        _server.InterceptingSubscriptionAsync += CheckSubscriptionAsync;
    }

    internal GuardedTcpServerAdapter Listener { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _server.StartAsync();
        _logger.LogInformation("MQTT gateway listening on port {Port}.", Listener.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stopping cancels every receive loop (the sessions' token is ApplicationStopping), and each
        // session then sends its client DISCONNECT 0x8B and announces its end while the bus is still up.
        _server.AcceptNewConnections = false;
        try
        {
            await Task.WhenAll(_running.Values).WaitAsync(StopGrace, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("{Count} MQTT sessions had not ended when the gateway stopped.", _running.Count);
        }

        await _server.StopAsync(new MqttServerStopOptions
        {
            DefaultClientDisconnectOptions = new MqttServerClientDisconnectOptions { ReasonCode = MqttDisconnectReasonCode.ServerShuttingDown }
        });
    }

    private async Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        var login = await _authenticator.AuthenticateAsync(
            new MqttConnectRequest(args.ProtocolVersion, args.ClientId, args.UserName, args.Password), args.CancellationToken);
        if (!login.IsAccepted)
        {
            args.ReasonCode = login.ReasonCode;
            return;
        }

        args.SessionItems[MqttConnection.SessionItemKey] = new MqttConnection(_server, login.Admission, _options.InboxCapacity)
        {
            Guard = Listener.GuardOf(args.ChannelAdapter)
        };
    }

    private async Task StartSessionAsync(ClientConnectedEventArgs args)
    {
        if (ConnectionOf(args.SessionItems) is not { } connection)
        {
            return;
        }

        // Subscribed before the session registers the client, so nothing sent to it is lost.
        await _server.SubscribeAsync(args.ClientId, [new MqttTopicFilterBuilder().WithTopic(connection.DownTopic).WithAtMostOnceQoS().Build()]);
        _running[connection] = Task.Run(() => RunSessionAsync(connection));
    }

    private async Task RunSessionAsync(MqttConnection connection)
    {
        try
        {
            await _sessions.RunAsync(connection.Admission, connection, inbox => connection.ReceiveAsync(inbox, _lifetime.ApplicationStopping));
        }
        finally
        {
            _running.TryRemove(connection, out _);
        }
    }

    private Task EndConnectionAsync(ClientDisconnectedEventArgs args)
    {
        if (ConnectionOf(args.SessionItems) is not { } connection)
        {
            return Task.CompletedTask;
        }

        var packetTooBig = connection.Guard?.Exceeded == true;
        if (packetTooBig)
        {
            _logger.LogWarning("Client {ClientRefId} sent an MQTT packet over the {MaxPacketBytes}-byte limit; closed.", args.ClientId, _options.MaxPacketBytes);
        }

        var reason = MqttDisconnectClassifier.Classify(args.DisconnectType, args.UserProperties, packetTooBig, _lifetime.ApplicationStopping.IsCancellationRequested);
        connection.Ended(reason);
        return Task.CompletedTask;
    }

    private async Task ReceivePublishAsync(InterceptingPublishEventArgs args)
    {
        // A message this host injected for a client's down topic carries no connection: let it route.
        if (ConnectionOf(args.SessionItems) is not { } connection)
        {
            return;
        }

        // Nothing a client publishes is routed: its own messages go to its session, anything else nowhere.
        args.ProcessPublish = false;
        if (args.ApplicationMessage.Topic != connection.UpTopic)
        {
            _logger.LogWarning("Client {ClientRefId} published to {Topic}, which is not its own; dropped.", args.ClientId, args.ApplicationMessage.Topic);
            args.Response.ReasonCode = MqttPubAckReasonCode.NotAuthorized;
            return;
        }

        await connection.EnqueueAsync(args.ApplicationMessage.ConvertPayloadToString(), args.CancellationToken);
    }

    private Task CheckSubscriptionAsync(InterceptingSubscriptionEventArgs args)
    {
        if (ConnectionOf(args.SessionItems) is { } connection && args.TopicFilter.Topic == connection.DownTopic)
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning("Client {MqttClientId} asked to subscribe to {Topic}, which is not its own; refused.", args.ClientId, args.TopicFilter.Topic);
        args.ProcessSubscription = false;
        args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
        return Task.CompletedTask;
    }

    private static MqttConnection? ConnectionOf(IDictionary? items) =>
        items?[MqttConnection.SessionItemKey] as MqttConnection;
}
