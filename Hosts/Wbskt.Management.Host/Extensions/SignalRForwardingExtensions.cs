using System.Reflection;
using MassTransit;
using Wbskt.Events;
using Wbskt.Management.Host.Handlers;

namespace Wbskt.Management.Host.Extensions;

public static class SignalRForwardingExtensions
{
    private static readonly Lazy<List<(Type ConsumerType, Type DefinitionType)>> ForwardingMappings = new(() =>
    {
        // Every [SignalRNotify] event lives in Wbskt.Events, the attribute's own assembly. Named rather
        // than found through the entry assembly's references, for the same reason AddRabbitMqEventBus
        // takes its consumer assemblies: under a test host the entry assembly is the test runner.
        return typeof(SignalRNotifyAttribute).Assembly
            .GetTypes()
            .Where(p => p.GetCustomAttribute<SignalRNotifyAttribute>() != null && p.IsClass)
            .Select(eventType => (
                ConsumerType: typeof(SignalRForwardingHandler<>).MakeGenericType(eventType),
                DefinitionType: typeof(SignalRForwarderDefinition<>).MakeGenericType(eventType)
            ))
            .ToList();
    });

    public static void AddAutoSignalRForwarding(this IBusRegistrationConfigurator configurator)
    {
        foreach (var mapping in ForwardingMappings.Value)
        {
            // Register the consumer along with its definition
            configurator.AddConsumer(mapping.ConsumerType, mapping.DefinitionType);
        }
    }
}
