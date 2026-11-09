using System.Diagnostics.CodeAnalysis;
using MQTTnet.Protocol;
using Wbskt.Gateway.Core.Services;

namespace Wbskt.Gateway.MQTT.Host.Models;

/// <summary>
/// How a CONNECT was answered: the admitted client, or the CONNACK reason code that refused it.
/// </summary>
public sealed record MqttLogin
{
    private MqttLogin(ClientAdmission? admission, MqttConnectReasonCode reasonCode)
    {
        Admission = admission;
        ReasonCode = reasonCode;
    }

    /// <summary>The client's admission, when it was accepted.</summary>
    public ClientAdmission? Admission { get; }

    public MqttConnectReasonCode ReasonCode { get; }

    [MemberNotNullWhen(true, nameof(Admission))]
    public bool IsAccepted => Admission is not null;

    public static MqttLogin Accept(ClientAdmission admission) => new(admission, MqttConnectReasonCode.Success);

    public static MqttLogin Refuse(MqttConnectReasonCode reasonCode) => new(null, reasonCode);
}
