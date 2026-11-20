using System.Text.Json.Serialization;

namespace Wbskt.Events.Client;

/// <summary>Why a known client's sign-in was refused.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientSignInRefusal>))]
public enum ClientSignInRefusal
{
    BadSecret,
    Pending,
    Revoked,
    Rejected
}
