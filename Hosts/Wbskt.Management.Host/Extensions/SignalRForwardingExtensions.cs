using System.Reflection;
using MassTransit;
using Wbskt.Events;
using Wbskt.Management.Host.Handlers;

namespace Wbskt.Management.Host.Extensions;

public static class SignalRForwardingExtensions
{
    private static readonly Lazy<List<(Type ConsumerType, Type DefinitionType)>> ForwardingMappings = new(() =>
    {
        // Scan WBSKT assemblies for events with the SignalRNotify attribute
        return Assembly.GetEntryAssembly()!
            .GetReferencedAssemblies()
            .Where(a => a.Name != null && a.Name.StartsWith("Wbskt"))
            .Select(Assembly.Load)
            .Append(Assembly.GetEntryAssembly()!)
            .SelectMany(s => s.GetTypes())
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
