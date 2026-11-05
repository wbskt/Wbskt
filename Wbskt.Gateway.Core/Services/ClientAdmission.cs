using System.Diagnostics.CodeAnalysis;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core.Services;

/// <summary>Why a client was refused before its connection was accepted.</summary>
public enum AdmissionRefusal
{
    /// <summary>The token authenticated but is not a well-formed client token: a claim is missing or unreadable.</summary>
    TokenInvalid,

    /// <summary>The client lost access after the token was issued: revoked, deleted, or its secret rotated.</summary>
    Revoked
}

/// <summary>
/// Whether a client may open a session, decided by <see cref="ClientSessions.AdmitAsync"/> the same
/// way for every transport. The transport turns a refusal into its own answer (an HTTP status before
/// a WebSocket upgrade, a CONNACK reason code for MQTT).
/// </summary>
public sealed record ClientAdmission
{
    private ClientAdmission(ClientIdentity? identity, AdmissionRefusal? refusal)
    {
        Identity = identity;
        Refusal = refusal;
    }

    /// <summary>Who the client is, when it was admitted.</summary>
    public ClientIdentity? Identity { get; }

    /// <summary>Why it was refused, when it was.</summary>
    public AdmissionRefusal? Refusal { get; }

    [MemberNotNullWhen(true, nameof(Identity))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsAdmitted => Identity != null;

    internal static ClientAdmission Admit(ClientIdentity identity) => new(identity, null);

    internal static ClientAdmission Refuse(AdmissionRefusal refusal) => new(null, refusal);
}
