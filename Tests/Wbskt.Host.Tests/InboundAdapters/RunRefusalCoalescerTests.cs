using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Host.Tests.InboundAdapters;

/// <summary>A burst at the run ceiling is one warning per workflow per window, not one per refusal (#432).</summary>
public sealed class RunRefusalCoalescerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkflowA = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");
    private static readonly Guid WorkflowB = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000000b");
    private static readonly DateTime T0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private readonly RunRefusalCoalescer _coalescer = new(TimeSpan.FromMinutes(1));

    [Fact]
    public void A_burst_is_one_warning_with_its_count_window_and_kinds()
    {
        for (int i = 0; i < 300; i++)
        {
            _coalescer.Report(Refusal(WorkflowA, i % 2 == 0 ? "webhook" : "client", T0.AddMilliseconds(i * 100)));
        }

        Assert.Empty(_coalescer.TakeDue(T0.AddSeconds(59)));
        WorkflowTriggerRefusedEvent warning = Assert.Single(_coalescer.TakeDue(T0.AddMinutes(1)));

        Assert.Equal(300, warning.RefusedCount);
        Assert.Equal(T0, warning.FirstRefusedAtUtc);
        Assert.Equal(T0.AddMilliseconds(299 * 100), warning.LastRefusedAtUtc);
        Assert.Equal("client,webhook", warning.TriggerKinds);
        Assert.Equal(200, warning.Ceiling);
        Assert.Equal((WorkflowA, 11, WorkspaceId), (warning.WorkflowRefId, warning.WorkflowId, warning.WorkspaceId));
    }

    [Fact]
    public void Each_workflow_gets_its_own_warning()
    {
        _coalescer.Report(Refusal(WorkflowA, "webhook", T0));
        _coalescer.Report(Refusal(WorkflowB, "schedule", T0));

        var warnings = _coalescer.TakeDue(T0.AddMinutes(1));

        Assert.Equal([WorkflowA, WorkflowB], warnings.Select(w => w.WorkflowRefId).Order());
    }

    [Fact]
    public void A_refusal_after_a_window_was_taken_starts_the_next_one()
    {
        _coalescer.Report(Refusal(WorkflowA, "webhook", T0));
        Assert.Single(_coalescer.TakeDue(T0.AddMinutes(1)));

        _coalescer.Report(Refusal(WorkflowA, "webhook", T0.AddMinutes(2)));

        Assert.Empty(_coalescer.TakeDue(T0.AddMinutes(2.5)));
        Assert.Equal(1, Assert.Single(_coalescer.TakeDue(T0.AddMinutes(3))).RefusedCount);
    }

    [Fact]
    public async Task On_shutdown_what_is_left_is_published_at_once()
    {
        var bus = new Mock<IEventBus>();
        var published = new List<WorkflowTriggerRefusedEvent>();
        bus.Setup(b => b.PublishAsync(It.IsAny<WorkflowTriggerRefusedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowTriggerRefusedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);
        var publisher = new RunRefusalPublisher(_coalescer, bus.Object, TimeProvider.System, NullLogger<RunRefusalPublisher>.Instance);
        _coalescer.Report(Refusal(WorkflowA, "webhook", DateTime.UtcNow));

        await publisher.PublishAsync(all: false, CancellationToken.None);
        Assert.Empty(published);

        await publisher.PublishAsync(all: true, CancellationToken.None);
        Assert.Single(published);
    }

    [Fact]
    public async Task A_bus_failure_is_logged_not_thrown()
    {
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<WorkflowTriggerRefusedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));
        var publisher = new RunRefusalPublisher(_coalescer, bus.Object, TimeProvider.System, NullLogger<RunRefusalPublisher>.Instance);
        _coalescer.Report(Refusal(WorkflowA, "webhook", DateTime.UtcNow));

        await publisher.PublishAsync(all: true, CancellationToken.None);
    }

    private static RunRefusal Refusal(Guid workflowRefId, string kind, DateTime at) => new(WorkspaceId, 11, workflowRefId, kind, 200, at);
}
