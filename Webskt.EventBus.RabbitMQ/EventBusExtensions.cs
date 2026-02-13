using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Data;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Implementations;
using Webskt.EventBus.Infrastructure;

namespace Webskt.EventBus.RabbitMQ;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(this IServiceCollection services, IConfiguration configuration,
        Action<RabbitMQOptions>? configure = null)
    {
        // 1. Core Event Bus Services
        services.AddEventBusCore();

        // 2. RabbitMQ Configuration
        services.Configure<RabbitMQOptions>(options =>
        {
            configuration.GetSection("RabbitMQ").Bind(options);
            configure?.Invoke(options);
        });

        // 3. Transport Implementation
        services.AddSingleton<IEventBus, RabbitMQBus>();
        services.AddHostedService<RabbitMQListener>();

        // 4. Persistence Implementation (From Common)
        services.AddWebsktDataServices();
        
        // 5. RabbitMQ Specific Startup Tasks
        services.AddTransient<IStartupTask, EventBusInitializationStartupTask>();
    }
}
