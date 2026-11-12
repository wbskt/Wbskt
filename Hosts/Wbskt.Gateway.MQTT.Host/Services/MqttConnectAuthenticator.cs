using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Gateway.MQTT.Host.Models;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.MQTT.Host.Services;

/// <summary>
/// Answers a CONNECT: the client's token is its password, the token's client must be both the client
/// id and the user name, and Gateway.Core's <see cref="ClientSessions.AdmitAsync"/> decides the rest,
/// as it does for the WebSocket gateway. Knows nothing of MQTTnet's server, so it is tested on its own.
/// </summary>
public sealed class MqttConnectAuthenticator
{
    private readonly ClientSessions _sessions;
    private readonly JwtTrust _trust;
    private readonly ILogger<MqttConnectAuthenticator> _logger;

    public MqttConnectAuthenticator(ClientSessions sessions, JwtTrust trust, ILogger<MqttConnectAuthenticator> logger)
    {
        _sessions = sessions;
        _trust = trust;
        _logger = logger;
    }

    public async Task<MqttLogin> AuthenticateAsync(MqttConnectRequest request, CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion is not (MqttProtocolVersion.V311 or MqttProtocolVersion.V500))
        {
            return MqttLogin.Refuse(MqttConnectReasonCode.UnsupportedProtocolVersion);
        }

        // A CONNECT that cannot name a client is refused before its token costs a signature check. Only
        // the usual form is taken: MQTTnet keys its sessions by the raw string, and the gateway finds a
        // connection to disconnect (a revoke, a delete, a rotated secret) by the usual form, so an upper-case
        // or braced id would leave a revoked client connected.
        if (!Guid.TryParseExact(request.ClientId, "D", out var claimedId) || request.ClientId != claimedId.ToString("D"))
        {
            return MqttLogin.Refuse(MqttConnectReasonCode.ClientIdentifierNotValid);
        }

        if (!Guid.TryParse(request.UserName, out var userName) || string.IsNullOrEmpty(request.Password))
        {
            return MqttLogin.Refuse(MqttConnectReasonCode.BadUserNameOrPassword);
        }

        ClaimsPrincipal user;
        try
        {
            user = await _trust.ValidateAsync(request.Password);
        }
        catch (SecurityTokenException)
        {
            return MqttLogin.Refuse(MqttConnectReasonCode.BadUserNameOrPassword);
        }
        catch (Exception ex)
        {
            // The issuer's keys could not be fetched: not the client's fault, so it should retry.
            _logger.LogWarning(ex, "Could not validate an MQTT client's token.");
            return MqttLogin.Refuse(MqttConnectReasonCode.ServerUnavailable);
        }

        var admission = await _sessions.AdmitAsync(user, cancellationToken);
        if (!admission.IsAdmitted)
        {
            return MqttLogin.Refuse(MqttReasonCodes.For(admission.Refusal.Value));
        }

        var clientRefId = admission.Identity.ClientRefId;

        // The client id is the client's RefId, so MQTT's own rule (one connection per client id) agrees
        // with ConnectionManager's, and a client cannot take over another's session.
        if (claimedId != clientRefId)
        {
            _logger.LogWarning("Refusing an MQTT connection for client {ClientRefId}: its client id is {MqttClientId}.", clientRefId, request.ClientId);
            return MqttLogin.Refuse(MqttConnectReasonCode.ClientIdentifierNotValid);
        }

        if (userName != clientRefId)
        {
            return MqttLogin.Refuse(MqttConnectReasonCode.BadUserNameOrPassword);
        }

        return MqttLogin.Accept(admission);
    }
}
