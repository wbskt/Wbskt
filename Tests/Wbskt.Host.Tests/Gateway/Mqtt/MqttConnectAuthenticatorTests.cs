using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Gateway.MQTT.Host.Models;
using Wbskt.Gateway.MQTT.Host.Services;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.Gateway.Mqtt;

/// <summary>How a CONNECT is answered, without a server or a socket.</summary>
public sealed class MqttConnectAuthenticatorTests : IDisposable
{
    private static readonly Guid ClientRefId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IClientAccessGate> _gate = new();
    private readonly JwtSigningKeys _keys = new(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem(), [], false, NullLogger.Instance);
    private readonly MqttConnectAuthenticator _authenticator;

    public MqttConnectAuthenticatorTests()
    {
        var metrics = new GatewayMetrics(GatewayTransport.Mqtt);
        var bus = new Mock<IEventBus>();
        var dispatcher = new InboundMessageDispatcher([], new ClientMessageHandler(bus.Object), metrics, NullLogger<InboundMessageDispatcher>.Instance);
        var sessions = new ClientSessions(new ConnectionManager(NullLogger<ConnectionManager>.Instance), _gate.Object, bus.Object, new BusInstanceId("gateway-mqtt"),
            metrics, dispatcher, Options.Create(new MessageBudgetOptions()), Mock.Of<IHostApplicationLifetime>(),
            NullLogger<ClientSessions>.Instance, NullLogger<ClientInbox>.Instance);
        var trust = JwtTrust.Local(JwtIssuers.Management, JwtAudiences.Socket, _keys.PublishedKeys);
        _authenticator = new MqttConnectAuthenticator(sessions, trust, NullLogger<MqttConnectAuthenticator>.Instance);
    }

    public void Dispose() => _keys.Dispose();

    [Fact]
    public async Task A_client_with_its_token_and_its_ref_id_is_accepted()
    {
        var login = await AuthenticateAsync();

        Assert.True(login.IsAccepted);
        Assert.Equal(ClientRefId, login.Admission.Identity?.ClientRefId);
        Assert.Equal(MqttConnectReasonCode.Success, login.ReasonCode);
    }

    [Fact]
    public async Task An_mqtt_31_client_is_refused_as_unsupported()
    {
        var login = await AuthenticateAsync(version: MqttProtocolVersion.V310);

        Assert.Equal(MqttConnectReasonCode.UnsupportedProtocolVersion, login.ReasonCode);
    }

    [Fact]
    public async Task A_client_id_that_is_not_a_ref_id_is_refused_before_access_is_checked()
    {
        var login = await AuthenticateAsync(clientId: "sensor-1");

        Assert.Equal(MqttConnectReasonCode.ClientIdentifierNotValid, login.ReasonCode);
        _gate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_password_is_refused_as_a_bad_password()
    {
        var login = await AuthenticateAsync(token: "");

        Assert.Equal(MqttConnectReasonCode.BadUserNameOrPassword, login.ReasonCode);
        _gate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_token_that_does_not_validate_is_refused_as_a_bad_password()
    {
        var login = await AuthenticateAsync(token: "not-a-token");

        Assert.Equal(MqttConnectReasonCode.BadUserNameOrPassword, login.ReasonCode);
        _gate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_revoked_client_is_refused_as_not_authorized()
    {
        _gate.Setup(x => x.IsRefusedAsync(ClientRefId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var login = await AuthenticateAsync();

        Assert.Equal(MqttConnectReasonCode.NotAuthorized, login.ReasonCode);
    }

    [Fact]
    public async Task Another_clients_ref_id_as_client_id_is_refused()
    {
        var login = await AuthenticateAsync(clientId: Guid.NewGuid().ToString());

        Assert.Equal(MqttConnectReasonCode.ClientIdentifierNotValid, login.ReasonCode);
    }

    [Fact]
    public async Task Another_clients_ref_id_as_user_name_is_refused_as_a_bad_user_name()
    {
        var login = await AuthenticateAsync(userName: Guid.NewGuid().ToString());

        Assert.Equal(MqttConnectReasonCode.BadUserNameOrPassword, login.ReasonCode);
    }

    private Task<MqttLogin> AuthenticateAsync(
        MqttProtocolVersion version = MqttProtocolVersion.V500,
        string? clientId = null,
        string? userName = null,
        string? token = null) =>
        _authenticator.AuthenticateAsync(
            new MqttConnectRequest(version, clientId ?? ClientRefId.ToString(), userName ?? ClientRefId.ToString(), token ?? Token()),
            CancellationToken.None);

    private string Token() =>
        new JwtService(_keys, JwtIssuers.Management).GenerateToken(ClientClaims.For(ClientRefId, "sensor", Guid.NewGuid(), 42, 7), JwtAudiences.Socket, TimeSpan.FromMinutes(15));
}
