using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Common.Data;

public static class DataServiceExtensions
{
    public static void AddWebsktEventDataServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventProvider, EventProvider>();
    }
}
