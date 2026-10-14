using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.EventBus.RabbitMQ;

// Also reused as the socket presence ConnectedHostId (see ConnectionSupersededHandler,
// SocketHostHeartbeat, Client_UpdatePresence.sql) - a connection's owning instance is
// identified by the same value used for its MassTransit queue name.
//
// This is deliberately NOT Wbskt.Workflow.Abstraction.Runtime.IHostIdentity, and the two must not
// be unified: IHostIdentity is intentionally regenerated (fresh GUID) on every process start, so
// a workflow-engine lease holder never appears to "continue" as the same identity across a crash
// - restarting must fence out the old instance, not reclaim it. BusInstanceId is the opposite: it
// must stay STABLE across a socket-host's restart (defaults to Environment.MachineName, i.e. the
// container's hostname), so a restarted instance comes back on its own per-instance queue and
// StartupPresenceResetPublisher republishes the id its previous run used, letting
// Client_ResetPresenceBy_HostId clear exactly the presence rows that run left dangling. Giving
// either identity the other's lifetime would break its own use case. (A container that is
// recreated, as every deploy does, gets a new hostname and so a new id; presence it left behind
// is cleared by the heartbeat lease instead, see SocketHostHeartbeat.)
public sealed record BusInstanceId(string Value);

public static class EventBusExtensions
{
    /// <summary>
    /// Registers the MassTransit bus over RabbitMQ, the <see cref="IEventBus"/> and the <see cref="ICommandSender"/>.
    /// </summary>
    /// <param name="consumerAssemblies">
    /// The assemblies whose consumers (and their definitions) this host runs, usually just its own
    /// (<c>typeof(Program).Assembly</c>). Left out, only the entry assembly is scanned. Nothing else is:
    /// a consumer added to a shared library runs only in the hosts that name that library here.
    /// </param>
    public static void AddRabbitMqEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<RabbitMQOptions>? configureOptions = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null,
        bool perInstanceEndpoints = false,
        IReadOnlyCollection<string>? endpointsWithOwnRetry = null,
        IReadOnlyCollection<Assembly>? consumerAssemblies = null)
        {

        var options = new RabbitMQOptions();
        configuration.GetSection("RabbitMQ").Bind(options);
        configureOptions?.Invoke(options);

        string? instanceId = null;
        if (perInstanceEndpoints)
        {
            instanceId = configuration["EventBus:InstanceId"] ?? Environment.MachineName;
            services.AddSingleton(new BusInstanceId(instanceId));
        }

        services.AddMassTransit(x =>
        {
            // 1. Allow the caller to add specific consumers/definitions
            configureBus?.Invoke(x);

            if (perInstanceEndpoints)
            {
                // Every instance gets its own auto-delete queue so ClientCommandEvent etc. fan out
                // to all socket hosts instead of round-robining across a shared competing queue.
                x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(instanceId!, false));
                x.AddConfigureEndpointsCallback((_, endpointConfigurator) =>
                {
                    if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqConfigurator)
                    {
                        rabbitMqConfigurator.Durable = false;
                        rabbitMqConfigurator.AutoDelete = true;
                    }
                });
            }

            x.AddDefaultConsumerRetry(endpointsWithOwnRetry);

            // 2. Register the consumers of the assemblies the host named
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            IReadOnlyCollection<Assembly> assemblies = consumerAssemblies
                ?? (entryAssembly != null ? new[] { entryAssembly } : Array.Empty<Assembly>());
            if (assemblies.Count > 0)
            {
                x.AddConsumers(assemblies.ToArray());
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.HostName, h =>
                {
                    h.Username(options.UserName);
                    h.Password(options.Password);
                });

                cfg.ConfigureEndpoints(context);
                configureEndpoints?.Invoke(context, cfg);
            });
        });

        services.AddSingleton<IEventBus, MassTransitEventBus>();
        services.AddSingleton<ICommandSender, MassTransitCommandSender>();
    }

    /// <summary>
    /// Gives every receive endpoint <see cref="ConsumerRetry.Default"/>, except those named in
    /// <paramref name="endpointsWithOwnRetry"/>, whose definitions configure a policy of their own (two
    /// retry filters on one endpoint would nest and multiply the attempts).
    /// </summary>
    /// <remarks>
    /// MassTransit does not retry unless asked: without this, one SQL timeout in a consumer moves the
    /// message straight to its _error queue, where it waits for someone to move it back.
    /// </remarks>
    public static void AddDefaultConsumerRetry(this IBusRegistrationConfigurator configurator, IReadOnlyCollection<string>? endpointsWithOwnRetry = null)
    {
        var excluded = new HashSet<string>(endpointsWithOwnRetry ?? [], StringComparer.Ordinal);
        configurator.AddConfigureEndpointsCallback((_, name, endpointConfigurator) =>
        {
            if (!excluded.Contains(name))
            {
                endpointConfigurator.UseMessageRetry(ConsumerRetry.Default);
            }
        });
    }
}
