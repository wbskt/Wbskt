using Microsoft.Extensions.DependencyInjection;
using Wbskt.Common.Providers;
using Wbskt.Common.Providers.Cache;
using Wbskt.Common.Providers.Implementations;
using Wbskt.Common.Providers.Readers;
using Wbskt.Common.Providers.Readers.Implementations;

namespace Wbskt.Common;

public static class DependencyInjection
{
    public static void ConfigureCommonServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IUsersProvider, UsersProvider>();
        serviceCollection.AddSingleton<IClientProvider, ClientProvider>();
        serviceCollection.AddSingleton<IChannelsProvider, ChannelsProvider>();
        serviceCollection.AddSingleton<IServerInfoProvider, ServerInfoProvider>();
        serviceCollection.AddSingleton<ICachedChannelsProvider, CachedChannelsProvider>();
        serviceCollection.AddSingleton<ICachedServerInfoProvider, CachedServerInfoProvider>();
        serviceCollection.AddSingleton<IConnectionStringProvider, ConnectionStringProvider>();

        //-------------------------
        serviceCollection.AddSingleton<IChannelsReader, CachedChannelsReader>();
        serviceCollection.AddSingleton<IDatabaseChangeListener, CachedChannelsReader>();
        serviceCollection.AddSingleton<IChannelsDatabaseReader, ChannelsDatabaseReader>();
    }
}
