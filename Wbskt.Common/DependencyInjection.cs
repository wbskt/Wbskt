using Microsoft.Extensions.DependencyInjection;
using Wbskt.Common.Providers;
using Wbskt.Common.Providers.Cache;
using Wbskt.Common.Providers.Implementations;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Implementations;
using Wbskt.Common.Writers;
using Wbskt.Common.Writers.Implementations;

namespace Wbskt.Common;

public static class DependencyInjection
{
    public static void ConfigureCommonServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IUsersProvider, UsersProvider>();
        serviceCollection.AddSingleton<IClientProvider, ClientProvider>();
        serviceCollection.AddSingleton<IServerInfoProvider, ServerInfoProvider>();
        serviceCollection.AddSingleton<ICachedServerInfoProvider, CachedServerInfoProvider>();
        serviceCollection.AddSingleton<IConnectionStringProvider, ConnectionStringProvider>();

        // Channel
        serviceCollection.AddSingleton<IChannelsWriter, ChannelsWriter>();
        serviceCollection.AddSingleton<IChannelsReader, CachedChannelsReader>();
        serviceCollection.AddSingleton<IChannelsDatabaseReader, ChannelsDatabaseReader>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedChannelsReader>(nameof(CachedChannelsReader));

        // Publisher
        serviceCollection.AddSingleton<IPublishersWriter, PublishersWriter>();
    }
}
