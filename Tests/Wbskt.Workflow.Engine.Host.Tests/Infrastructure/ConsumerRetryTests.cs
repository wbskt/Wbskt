using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.RabbitMQ;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// The bus-wide retry from <see cref="EventBusExtensions.AddDefaultConsumerRetry"/>, on an in-memory bus
/// (the policy is attached to endpoints, so the transport does not matter).
/// </summary>
public sealed class ConsumerRetryTests
{
    [Fact]
    public async Task A_consumer_that_fails_once_is_retried_and_succeeds()
    {
        var probe = new Probe(failuresBeforeSuccess: 1, failure: () => new InvalidOperationException("SQL timeout"));

        await RunAsync(probe, endpointsWithOwnRetry: null);

        Assert.Equal(2, probe.Attempts);
        Assert.True(probe.Succeeded);
    }

    [Fact]
    public async Task Input_that_can_never_succeed_is_not_retried()
    {
        var probe = new Probe(failuresBeforeSuccess: int.MaxValue, failure: () => new ArgumentException("bad payload"));

        await RunAsync(probe, endpointsWithOwnRetry: null);

        Assert.Equal(1, probe.Attempts);
    }

    [Fact]
    public async Task An_endpoint_with_its_own_policy_is_left_alone()
    {
        var probe = new Probe(failuresBeforeSuccess: 1, failure: () => new InvalidOperationException("SQL timeout"));

        await RunAsync(probe, endpointsWithOwnRetry: [ProbeConsumerDefinition.QueueName]);

        Assert.Equal(1, probe.Attempts);
        Assert.False(probe.Succeeded);
    }

    private static async Task RunAsync(Probe probe, IReadOnlyCollection<string>? endpointsWithOwnRetry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMassTransit(x =>
        {
            x.AddDefaultConsumerRetry(endpointsWithOwnRetry);
            x.AddConsumer<ProbeConsumer, ProbeConsumerDefinition>();
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await bus.Publish(new ProbeMessage());

            // The first retry waits about a second; anything still pending after that is not coming.
            await probe.Settled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    public sealed record ProbeMessage;

    public sealed class Probe(int failuresBeforeSuccess, Func<Exception> failure)
    {
        private int _attempts;

        public int Attempts => _attempts;

        public bool Succeeded { get; private set; }

        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Handle()
        {
            int attempt = Interlocked.Increment(ref _attempts);
            Settled.TrySetResult();
            if (attempt <= failuresBeforeSuccess)
            {
                throw failure();
            }

            Succeeded = true;
        }
    }

    public sealed class ProbeConsumer(Probe probe) : IConsumer<ProbeMessage>
    {
        public Task Consume(ConsumeContext<ProbeMessage> context)
        {
            probe.Handle();
            return Task.CompletedTask;
        }
    }

    public sealed class ProbeConsumerDefinition : ConsumerDefinition<ProbeConsumer>
    {
        public const string QueueName = "consumer-retry-probe";

        public ProbeConsumerDefinition()
        {
            EndpointName = QueueName;
        }
    }
}
