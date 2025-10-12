using Microsoft.Extensions.DependencyInjection;
using Wbskt.Common.Providers;
using Wbskt.Common.Providers.Cache;
using Wbskt.Common.Providers.Implementations;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Cache;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Writers;
using Wbskt.Common.Writers.Implementations;

namespace Wbskt.Common;

public static class DependencyInjection
{
    public static void ConfigureCommonServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IUsersProvider, UsersProvider>();
        serviceCollection.AddSingleton<IServerInfoProvider, ServerInfoProvider>();
        serviceCollection.AddSingleton<ICachedServerInfoProvider, CachedServerInfoProvider>();
        serviceCollection.AddSingleton<IConnectionStringProvider, ConnectionStringProvider>();

        // Channels
        serviceCollection.AddSingleton<IChannelsWriter, ChannelsWriter>();
        serviceCollection.AddSingleton<IChannelsReader, CachedChannelsReader>();
        serviceCollection.AddSingleton<IChannelsDatabaseReader, ChannelsDatabaseReader>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedChannelsReader>(nameof(CachedChannelsReader));

        // Publishers
        serviceCollection.AddSingleton<IPublishersWriter, PublishersWriter>();
        serviceCollection.AddSingleton<IPublishersDatabaseReader, PublishersDatabaseReader>();
        serviceCollection.AddSingleton<IPublishersReader, CachedPublishersReader>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedPublishersReader>(nameof(CachedPublishersReader));

        // Publishers-Channels
        serviceCollection.AddSingleton<IPublishersChannelsDatabaseReader, PublishersChannelsDatabaseReader>();
        serviceCollection.AddSingleton<IPublishersChannelsReader, CachedPublishersChannelsReader>();
        serviceCollection.AddSingleton<IPublishersChannelsWriter, PublishersChannelsWriter>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedPublishersChannelsReader>(nameof(CachedPublishersChannelsReader));

        // Clients
        serviceCollection.AddSingleton<IClientsWriter, ClientsWriter>();
        serviceCollection.AddSingleton<IClientsDatabaseReader, ClientsDatabaseReader>();
        serviceCollection.AddSingleton<IClientsReader, CachedClientsReader>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedClientsReader>(nameof(CachedClientsReader));

        // Clients-Channels
        serviceCollection.AddSingleton<IClientsChannelsDatabaseReader, ClientsChannelsDatabaseReader>();
        serviceCollection.AddSingleton<IClientsChannelsReader, CachedClientsChannelsReader>();
        serviceCollection.AddSingleton<IClientsChannelsWriter, ClientsChannelsWriter>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedClientsChannelsReader>(nameof(CachedClientsChannelsReader));

        // Registration Policies
        serviceCollection.AddSingleton<IRegistrationPoliciesWriter, RegistrationPoliciesWriter>();
        serviceCollection.AddSingleton<IRegistrationPoliciesDatabaseReader, RegistrationPoliciesDatabaseReader>();
        serviceCollection.AddSingleton<IRegistrationPoliciesReader, CachedRegistrationPoliciesReader>();
        serviceCollection.AddKeyedSingleton<IDatabaseChangeListener, CachedRegistrationPoliciesReader>(nameof(CachedRegistrationPoliciesReader));
    }
}

public class DatabaseChangeListenerRegistry
{
    private readonly string[] registeredKeys = new[]
    {
        nameof(CachedChannelsReader),
        nameof(CachedPublishersReader),
        nameof(CachedPublishersChannelsReader),
        nameof(CachedClientsReader),
        nameof(CachedClientsChannelsReader),
        nameof(CachedRegistrationPoliciesReader)
    };

    public IEnumerable<IDatabaseChangeListener> GetAllListeners(IServiceProvider serviceProvider)
    {
        return registeredKeys.Select(serviceProvider.GetRequiredKeyedService<IDatabaseChangeListener>);
    }
}
