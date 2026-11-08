namespace Wbskt.Protocol;

/// <summary>What is particular to carrying the message model over MQTT (Docs/Client.Protocol.MQTT.md).</summary>
public static class MqttBinding
{
    /// <summary>The root of every topic this version of the protocol uses.</summary>
    public const string TopicPrefix = "wbskt/v1/";

    /// <summary>
    /// The user property an MQTT 5 client puts on its DISCONNECT to say why it is closing. Its only
    /// value so far is <see cref="UpdatingReason"/>.
    /// </summary>
    public const string CloseReasonProperty = "reason";

    /// <summary>
    /// The <see cref="CloseReasonProperty"/> of a client closing to install a firmware update. The
    /// gateway reports the client as updating rather than offline, for up to
    /// <see cref="OtaTiming.UpdateWindow"/>. MQTT 3.1.1 has no properties on DISCONNECT, so a 3.1.1
    /// client's update reads as an ordinary close.
    /// </summary>
    public const string UpdatingReason = "updating";

    /// <summary>The topic a client publishes its messages to, and the only one it may publish to.</summary>
    public static string UpTopic(Guid clientRefId) => $"{TopicPrefix}{clientRefId:D}/up";

    /// <summary>
    /// The topic the gateway sends a client's messages on. The gateway subscribes the client to it as
    /// it connects; a client may subscribe to it too, and to nothing else.
    /// </summary>
    public static string DownTopic(Guid clientRefId) => $"{TopicPrefix}{clientRefId:D}/down";
}
