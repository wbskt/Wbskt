using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Protocol;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>
/// One client's MQTT connection. MQTTnet owns the socket and its packet loop; this holds what the
/// session needs from it. The client's PUBLISHes arrive through <see cref="EnqueueAsync"/> and are
/// read by the session's receive loop (<see cref="ReceiveAsync"/>); messages to the client go out on
/// its down topic; closing it is a DISCONNECT with the reason's code.
/// </summary>
/// <remarks>
/// Several connections can share one MQTT client id over time: a reconnect takes the id over while
/// the older connection's session is still ending. So this connection closes the client id only while
/// the server's session for it is still this connection's own, never the newer one's.
/// </remarks>
public sealed class MqttConnection : ClientConnectionBase
{
    /// <summary>Where a connection keeps itself in its MQTTnet session's items.</summary>
    public const string SessionItemKey = "wbskt.connection";

    private readonly MqttServer _server;
    private readonly Channel<string> _inbound;
    private readonly object _endGate = new();
    private DisconnectReason? _ended;

    [SetsRequiredMembers]
    public MqttConnection(MqttServer server, ClientAdmission admission, int inboxCapacity)
    {
        if (!admission.IsAdmitted)
        {
            throw new ArgumentException($"A refused client ({admission.Refusal}) has no connection.", nameof(admission));
        }

        _server = server;
        Admission = admission;
        ClientId = admission.Identity.ClientId;
        WorkspaceId = admission.Identity.WorkspaceId;
        MqttClientId = admission.Identity.ClientRefId.ToString("D");
        UpTopic = MqttBinding.UpTopic(admission.Identity.ClientRefId);
        DownTopic = MqttBinding.DownTopic(admission.Identity.ClientRefId);
        _inbound = Channel.CreateBounded<string>(new BoundedChannelOptions(inboxCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public ClientAdmission Admission { get; }

    /// <summary>The MQTT client id, which is the client's RefId.</summary>
    public string MqttClientId { get; }

    public string UpTopic { get; }

    public string DownTopic { get; }

    /// <summary>The guard reading this connection's bytes, to tell an oversized packet from a dropped connection.</summary>
    public PacketSizeGuardStream? Guard { get; init; }

    public override bool IsOpen
    {
        get
        {
            lock (_endGate)
            {
                return _ended is null;
            }
        }
    }

    /// <summary>
    /// Queues one message the client published, for the receive loop. Waits while the loop is behind,
    /// which holds back this client's packet loop and no one else's. A message arriving after the
    /// connection ended is dropped.
    /// </summary>
    public async ValueTask EnqueueAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await _inbound.Writer.WriteAsync(text, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // The session is over; nothing reads this any more.
        }
    }

    /// <summary>
    /// The session's receive loop: hands each message to the inbox until the connection ends or the
    /// inbox ends the session, and returns why.
    /// </summary>
    public async Task<DisconnectReason> ReceiveAsync(ClientInbox inbox, CancellationToken cancellationToken)
    {
        await foreach (var text in _inbound.Reader.ReadAllAsync(cancellationToken))
        {
            if (await inbox.ReceiveAsync(text, cancellationToken) is { } ended)
            {
                return ended;
            }
        }

        lock (_endGate)
        {
            return _ended ?? DisconnectReason.Aborted;
        }
    }

    /// <summary>Records that the connection is gone, and why, and lets the receive loop finish. The first reason holds.</summary>
    public void Ended(DisconnectReason reason)
    {
        lock (_endGate)
        {
            _ended ??= reason;
        }

        _inbound.Writer.TryComplete();
    }

    public override Task CloseTransportAsync(DisconnectReason reason) => TryDisconnectAsync(reason, reason.Describe());

    protected override Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        // An injected message with no subscriber is dropped without an error, so a send to a client
        // that has gone must fail here, as a socket's would, for a command to be refused rather than lost.
        if (!IsOpen)
        {
            return Task.FromException(new InvalidOperationException("The client's MQTT connection has ended."));
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(DownTopic)
            .WithPayload(Encoding.UTF8.GetBytes(text))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .WithContentType("application/json")
            .Build();

        return _server.InjectApplicationMessage(new InjectedMqttApplicationMessage(message), cancellationToken);
    }

    protected override async Task CloseAsync(DisconnectReason reason, string description)
    {
        await TryDisconnectAsync(reason, description);
        Ended(reason);
    }

    // Never throws: the connection is going away whether or not the DISCONNECT could be sent.
    private async Task TryDisconnectAsync(DisconnectReason reason, string description)
    {
        try
        {
            await DisconnectAsync(reason, description);
        }
        catch
        {
            // Nothing to do: the client is gone or the server is stopping.
        }
    }

    private async Task DisconnectAsync(DisconnectReason reason, string description)
    {
        // Already gone: the client disconnected, or an earlier close ended it.
        if (!IsOpen)
        {
            return;
        }

        var session = await _server.GetSessionAsync(MqttClientId);
        if (session?.Items[SessionItemKey] is not MqttConnection current || !ReferenceEquals(current, this))
        {
            // A newer connection holds the client id; this one is already gone.
            return;
        }

        await _server.DisconnectClientAsync(MqttClientId, new MqttServerClientDisconnectOptions
        {
            ReasonCode = MqttReasonCodes.For(reason),
            ReasonString = description
        });
    }
}
