using Microsoft.Extensions.DependencyInjection;

namespace Webskt.EventBus;

public static class EventBusServiceExtensions
{
    public static void AddEventBusCore(this IServiceCollection services)
    {
        // Core initialization logic for EventBus if any.
        // Currently MassTransit handles most things.
    }
}
