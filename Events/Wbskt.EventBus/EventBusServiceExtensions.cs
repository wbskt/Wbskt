using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.EventBus;

public static class EventBusServiceExtensions
{
    public static void AddEventBusCore(this IServiceCollection services)
    {
        // Add common, non-DB specific event bus registrations here
    }
}
