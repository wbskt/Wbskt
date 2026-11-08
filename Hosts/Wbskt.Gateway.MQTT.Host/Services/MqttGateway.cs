using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Protocol;

namespace Wbskt.Gateway.MQTT.Host.Services;

/// <summary>
/// The MQTT transport: an embedded MQTTnet server that is not a broker. It authenticates CONNECT with
/// the client token, holds each client to its own two topics, and hands every PUBLISH to the client's
/// session instead of routing it. Who may connect and what a session means (registering the client,
/// announcing it, budgeting and routing its messages, recording its end) is Gateway.Core's
/// <see cref="ClientSessions"/>, the same as for the WebSocket gateway.
/// </summary>
public sealed class MqttGateway : IHostedService
{
    // How long stopping waits for sessions to announce their end before the server goes.
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly ClientSessions _sessions;
    private readonly JwtTrust _trust;
    private readonly MqttGatewayOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MqttGateway> _logger;
    private readonly ConcurrentDictionary<MqttConnection, Task> _running = new();

    public MqttGateway(
        ClientSessions sessions,
        JwtTrust trust,
        IOptions<MqttGatewayOptions> options,
        IHostApplicationLifetime lifetime,
        MqttNetLoggerBridge mqttLogger,
        ILogger<MqttGateway> logger)
    {
        _sessions = sessions;
        _trust = trust;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;

        Listener = new GuardedTcpServerAdapter(_options.Port, _options.MaxPacketBytes);
        var serverOptions = new MqttServerOptionsBuilder()
            .WithPersistentSessions(false)
            .Build();
        Server = new MqttServerFactory().CreateMqttServer(serverOptions, [Listener], mqttLogger);
        Server.ValidatingConnectionAsync += ValidateConnectionAsync;
        Server.ClientConnectedAsync += StartSessionAsync;
        Server.ClientDisconnectedAsync += EndConnectionAsync;
        Server.InterceptingPublishAsync += ReceivePublishAsync;
        Server.InterceptingSubscriptionAsync += CheckSubscriptionAsync;
    }

    public MqttServer Server { get; }

    public GuardedTcpServerAdapter Listener { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Server.StartAsync();
        _logger.LogInformation("MQTT gateway listening on port {Port}.", Listener.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stopping cancels every receive loop (the sessions' token is ApplicationStopping), and each
        // session then sends its client DISCONNECT 0x8B and announces its end while the bus is still up.
        Server.AcceptNewConnections = false;
        try
        {
            await Task.WhenAll(_running.Values).WaitAsync(StopGrace, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("{Count} MQTT sessions had not ended when the gateway stopped.", _running.Count);
        }

        await Server.StopAsync(new MqttServerStopOptions
        {
            DefaultClientDisconnectOptions = new MqttServerClientDisconnectOptions { ReasonCode = MqttDisconnectReasonCode.ServerShuttingDown }
        });
    }

    private async Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (args.ProtocolVersion is not (MqttProtocolVersion.V311 or MqttProtocolVersion.V500))
        {
            args.ReasonCode = MqttConnectReasonCode.UnsupportedProtocolVersion;
            return;
        }

        if (string.IsNullOrEmpty(args.Password))
        {
            args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return;
        }

        System.Security.Claims.ClaimsPrincipal user;
        try
        {
            user = await _trust.ValidateAsync(args.Password);
        }
        catch (SecurityTokenException)
        {
            args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return;
        }
        catch (Exception ex)
        {
            // The issuer's keys could not be fetched: not the client's fault, so it should retry.
            _logger.LogWarning(ex, "Could not validate an MQTT client's token.");
            args.ReasonCode = MqttConnectReasonCode.ServerUnavailable;
            return;
        }

        var admission = await _sessions.AdmitAsync(user, args.CancellationToken);
        if (!admission.IsAdmitted)
        {
            args.ReasonCode = MqttReasonCodes.For(admission.Refusal.Value);
            return;
        }

        var clientRefId = admission.Identity.ClientRefId;

        // The client id is the client's RefId, so MQTT's own rule (one connection per client id) agrees
        // with ConnectionManager's, and a client cannot take over another's session.
        if (!Guid.TryParse(args.ClientId, out var claimedId) || claimedId != clientRefId)
        {
            _logger.LogWarning("Refusing an MQTT connection for client {ClientRefId}: its client id is {MqttClientId}.", clientRefId, args.ClientId);
            args.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
            return;
        }

        if (!Guid.TryParse(args.UserName, out var userName) || userName != clientRefId)
        {
            args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return;
        }

        args.SessionItems[MqttConnection.SessionItemKey] = new MqttConnection(Server, admission, _options.InboxCapacity)
        {
            ClientId = admission.Identity.ClientId,
            WorkspaceId = admission.Identity.WorkspaceId,
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
        await Server.SubscribeAsync(args.ClientId, [new MqttTopicFilterBuilder().WithTopic(connection.DownTopic).WithAtMostOnceQoS().Build()]);
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

        DisconnectReason reason;
        if (connection.Guard?.Exceeded == true)
        {
            _logger.LogWarning("Client {ClientRefId} sent an MQTT packet over the {MaxPacketBytes}-byte limit; closed.", args.ClientId, _options.MaxPacketBytes);
            reason = DisconnectReason.MessageTooBig;
        }
        else
        {
            reason = args.DisconnectType switch
            {
                MqttClientDisconnectType.Takeover => DisconnectReason.Superseded,
                MqttClientDisconnectType.Clean when IsUpdating(args) => DisconnectReason.Updating,
                MqttClientDisconnectType.Clean => DisconnectReason.ClientClosed,
                _ when _lifetime.ApplicationStopping.IsCancellationRequested => DisconnectReason.ServerStopping,
                _ => DisconnectReason.Aborted
            };
        }

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

    // A user property rather than the reason string, which MQTTnet does not pass on to the server.
    private static bool IsUpdating(ClientDisconnectedEventArgs args) =>
        args.UserProperties?.Any(p => p.Name == MqttBinding.CloseReasonProperty && p.ReadValueAsString() == MqttBinding.UpdatingReason) == true;

    private static MqttConnection? ConnectionOf(System.Collections.IDictionary? items) =>
        items?[MqttConnection.SessionItemKey] as MqttConnection;
}
