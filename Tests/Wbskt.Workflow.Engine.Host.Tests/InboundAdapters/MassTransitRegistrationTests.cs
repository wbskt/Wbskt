using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.RabbitMQ;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class MassTransitRegistrationTests
{
    [Fact]
    public void AddRabbitMqEventBus_registers_IBus()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = "localhost",
                ["RabbitMQ:UserName"] = "guest",
                ["RabbitMQ:Password"] = "guest"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqEventBus(config);

        ServiceDescriptor? descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IBus));
        Assert.NotNull(descriptor);
    }
}
