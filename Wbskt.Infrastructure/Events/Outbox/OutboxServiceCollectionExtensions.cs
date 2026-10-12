using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.Events.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Publishes the <c>dbo.OutboxMessages</c> of the database behind <paramref name="connectionStringName"/>.
    /// Register it after the event bus, so that on shutdown it stops before the bus does.
    /// </summary>
    public static IServiceCollection AddEventOutbox(this IServiceCollection services, string connectionStringName)
    {
        services.AddSingleton<IOutboxStore>(sp => new SqlOutboxStore(sp.GetRequiredService<IConfiguration>(), connectionStringName));
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}
