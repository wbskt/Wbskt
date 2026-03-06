using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wbskt.Common.Abstraction.Interfaces;

namespace Wbskt.Common.Data;

public static class DataServiceExtensions
{
    public static void AddWbsktEventDataServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventProvider, EventProvider>();
    }
}
