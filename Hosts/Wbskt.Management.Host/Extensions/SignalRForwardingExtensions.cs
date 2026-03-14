using System.Reflection;
using MassTransit;
using Wbskt.Common.Abstraction;
using Wbskt.Management.Host.Handlers;

namespace Wbskt.Management.Host.Extensions;

public static class SignalRForwardingExtensions
{
    public static void AddAutoSignalRForwarding(this IBusRegistrationConfigurator configurator)
    {
        // Scan for all event types that have the SignalRNotifyAttribute
        var eventTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => p.GetCustomAttribute<SignalRNotifyAttribute>() != null && p.IsClass);

        foreach (var eventType in eventTypes)
        {
            // Dynamically create the generic consumer type: SignalRForwardingConsumer<TEvent>
            var consumerType = typeof(SignalRForwardingConsumer<>).MakeGenericType(eventType);
            
            // Register the consumer
            configurator.AddConsumer(consumerType);
        }
    }

    public static void ConfigureAutoSignalRForwardingEndpoints(this IRabbitMqBusFactoryConfigurator cfg, IRegistrationContext context)
    {
        var eventTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => p.GetCustomAttribute<SignalRNotifyAttribute>() != null && p.IsClass);

        foreach (var eventType in eventTypes)
        {
            var consumerType = typeof(SignalRForwardingConsumer<>).MakeGenericType(eventType);

            // Create a unique queue name for this forwarder
            var queueName = $"signalr-forwarder-{eventType.Name.ToLower()}";

            cfg.ReceiveEndpoint(queueName, e =>
            {
                e.ConfigureConsumer(context, consumerType);
            });
        }
    }
}
