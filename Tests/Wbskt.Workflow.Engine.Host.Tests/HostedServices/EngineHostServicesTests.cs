using Microsoft.Extensions.Configuration;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class EngineHostServicesTests
{
    [Fact]
    public void The_engine_hosts_registrations_build_with_every_dependency_resolvable()
    {
        // What the host registers, plus what Main and the generic host add from outside it (the bus, the
        // host lifetime, HTTP clients, logging).
        // ValidateOnBuild checks every registration's constructor can be satisfied, without constructing
        // anything, so a missing or circular dependency fails here rather than on the first request.
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<IBusControl>());
        services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        services.AddEngineHostServices(configuration);

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
