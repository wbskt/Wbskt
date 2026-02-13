using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.Common.Data;

public static class DataServiceExtensions
{
    public static void AddWebsktDataServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventProvider, EventProvider>();
    }
}
