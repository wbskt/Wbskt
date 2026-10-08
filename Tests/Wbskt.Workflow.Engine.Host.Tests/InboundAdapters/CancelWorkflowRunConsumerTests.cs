using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class CancelWorkflowRunConsumerTests
{
    [Fact]
    public async Task Consume_hands_the_command_to_the_run_cancellation_service()
    {
        var cancellation = new Mock<IRunCancellationService>();
        cancellation.Setup(s => s.RequestCancellationAsync(42, "operator", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await new CancelWorkflowRunConsumer(cancellation.Object).Consume(Context(42, "operator"));

        cancellation.Verify(s => s.RequestCancellationAsync(42, "operator", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_treats_a_run_that_finished_in_the_meantime_as_done()
    {
        // Management read the run as cancellable, but it can still finish before the command arrives.
        // Throwing would only retry it and then park it on the error queue for nothing.
        var cancellation = new Mock<IRunCancellationService>();
        cancellation.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await new CancelWorkflowRunConsumer(cancellation.Object).Consume(Context(42, "operator"));

        cancellation.Verify(s => s.RequestCancellationAsync(42, "operator", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_redelivered_command_cancels_the_run_once()
    {
        // The real cancellation service over a run row that changes as the engine works: the first
        // delivery moves it to 'Cancelling', records it and announces it; a redelivery while it is still
        // cancelling, and another after it reached 'Cancelled', record and announce nothing more.
        var run = new StatefulRun("Running");
        var history = new Mock<IHistoryEventProvider>();
        var bus = new Mock<IEventBus>();
        var service = new RunCancellationService(
            run.Provider.Object,
            history.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IClock>(c => c.UtcNow == new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
            Mock.Of<IServiceProvider>(),
            eventBus: bus.Object);
        var consumer = new CancelWorkflowRunConsumer(service);

        await consumer.Consume(Context(42, "operator"));
        Assert.Equal("Cancelling", run.Status);

        await consumer.Consume(Context(42, "operator"));
        Assert.Equal("Cancelling", run.Status);

        run.Status = "Cancelled";
        await consumer.Consume(Context(42, "operator"));
        Assert.Equal("Cancelled", run.Status);

        history.Verify(h => h.InsertBatchAsync(It.IsAny<IReadOnlyCollection<HistoryEventRow>>(), It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(It.IsAny<WorkflowRunCancellationRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_engine_hosts_consumer_scan_puts_the_command_on_one_shared_queue()
    {
        // Registered the way AddRabbitMqEventBus registers the engine's consumers (an assembly scan,
        // which also picks up the definition): a single named queue every engine instance competes on,
        // so each command is handled once rather than once per instance.
        var services = new ServiceCollection();
        services.AddMassTransit(x =>
        {
            x.AddConsumers(typeof(CancelWorkflowRunConsumer).Assembly);
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();

        await bus.StartAsync();
        string endpoints = JsonSerializer.Serialize(bus.GetProbeResult());
        await bus.StopAsync();

        Assert.Contains(CancelWorkflowRunConsumerDefinition.QueueName, endpoints);
    }

    private static ConsumeContext<CancelWorkflowRun> Context(long runId, string reason)
    {
        var context = new Mock<ConsumeContext<CancelWorkflowRun>>();
        context.SetupGet(c => c.Message).Returns(new CancelWorkflowRun(runId, reason));
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    /// <summary>A run row whose status moves only through a compare-and-set, as Run_TransitionStatus does.</summary>
    private sealed class StatefulRun
    {
        public StatefulRun(string status)
        {
            Status = status;
            Provider.Setup(p => p.TransitionStatusAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long _, string from, string to, DateTime? _, string? _, CancellationToken _) =>
                {
                    if (!string.Equals(Status, from, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    Status = to;
                    return true;
                });
            Provider.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Row());
        }

        public string Status { get; set; }

        public Mock<IRunProvider> Provider { get; } = new();

        private RunRow Row() => new()
        {
            Id = 42,
            RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowDefinitionId = 9,
            WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CorrelationKey = "corr-42",
            Status = Status,
            StartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
