using Microsoft.Extensions.DependencyInjection.Extensions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Gateway.Core.HostedServices;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core;

public static class GatewayCoreExtensions
{
    /// <summary>
    /// Everything a gateway host needs besides its transport: client sessions, token trust and
    /// revocation, the inbound message handlers, the event bus with the consumers that deliver
    /// commands, pings and access changes to connected clients, and the presence and latency
    /// background services. The host adds its transport and authentication, and the
    /// <see cref="GatewayMetrics.MeterName"/> meter to its telemetry.
    /// </summary>
    public static IServiceCollection AddGatewayCore(this IServiceCollection services, IConfiguration configuration, GatewayTransport transport)
    {
        services.AddSingleton(transport);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<GatewayMetrics>();

        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<IRevocationCache, RevocationCache>();
        // The durable copy of the same revocations, written by the management host; see ClientTokenCutoffs.
        services.AddClientTokenCutoffs(configuration);
        services.AddSingleton<IClientAccessGate, ClientAccessGate>();
        // Signs nothing; accepts client tokens signed by the management host (Jwt:TrustedJwksUrl).
        // Every transport takes the same client token.
        services.AddWbsktJwtTrust(JwtIssuers.Management, JwtAudiences.Socket);
        services.AddSingleton<SupersedeMonitor>();
        services.AddSingleton<ClientSessions>();
        // What a client sends: one handler per protocol message type, and the client's own messages.
        services.AddSingleton<IInboundMessageHandler, PongHandler>();
        services.AddSingleton<IInboundMessageHandler, AckHandler>();
        services.AddSingleton<IInboundMessageHandler, OtaStatusHandler>();
        services.AddSingleton<ClientMessageHandler>();
        services.AddSingleton<InboundMessageDispatcher>();
        services.Configure<MessageBudgetOptions>(configuration.GetSection(MessageBudgetOptions.SectionName));

        // Event Bus — per-instance fan-out so every gateway instance sees every client event
        // (ClientCommandEvent, ClientPingEvent, ClientStatusChangedEvent); see EventBusExtensions.
        services.AddRabbitMqEventBus(configuration, perInstanceEndpoints: true, consumerAssemblies: [typeof(GatewayCoreExtensions).Assembly]);

        // Background services (registered after the bus so it starts first)
        services.AddHostedService<StartupPresenceResetPublisher>();
        services.AddHostedService<HeartbeatPublisher>();
        services.Configure<PingSamplerOptions>(configuration.GetSection(PingSamplerOptions.SectionName));
        services.AddHostedService<PingSampler>();

        return services;
    }
}
