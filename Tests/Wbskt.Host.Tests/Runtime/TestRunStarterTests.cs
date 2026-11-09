using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class TestRunStarterTests
{
    private static readonly Guid Trigger = Guid.NewGuid();
    private static readonly Guid WorkflowRef = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<string, JsonElement> Payload = new Dictionary<string, JsonElement> { ["body"] = JsonSerializer.SerializeToElement(1) };

    private readonly Mock<IRunStarter> _runStarter = new();
    private readonly Mock<IRunDispatcher> _dispatcher = new();
    private readonly Mock<IRunProvider> _runs = new();

    [Fact]
    public async Task A_started_test_run_has_a_key_of_its_own_and_its_branch_is_set_going()
    {
        Guid runRef = Guid.NewGuid();
        InboundEvent? seen = null;
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .Callback((int _, string _, InboundEvent evt, CancellationToken _) => seen = evt)
            .ReturnsAsync(new RunStartResult(11, runRef, 12, true));

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(new TestRunStart(TestRunStartOutcome.Started, runRef), started);
        Assert.Equal(TestRunStarter.ChannelKind, seen!.ChannelKind);
        Assert.StartsWith("test:", seen.CorrelationKey);
        Assert.Same(Payload, seen.Payload);
        _dispatcher.Verify(x => x.DispatchAsync(new BranchExecutionRequest(11, 12, BranchExecutionReason.TriggerStarted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_second_test_run_names_the_one_going()
    {
        Guid active = Guid.NewGuid();
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TestRunActiveException(WorkflowRef));
        _runs.Setup(x => x.FindActiveTestRunAsync(WorkflowRef, It.IsAny<CancellationToken>())).ReturnsAsync(active);

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(new TestRunStart(TestRunStartOutcome.TestRunActive, ActiveRunRefId: active), started);
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_full_workspace_starts_nothing()
    {
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanRunLimitException(3, 10));

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(TestRunStartOutcome.WorkspaceFull, started.Outcome);
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Spent_credits_start_nothing()
    {
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanCreditLimitException(3, 2000m));

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(TestRunStartOutcome.CreditsSpent, started.Outcome);
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_spent_workspace_cap_starts_nothing()
    {
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkspaceCreditCapException(3));

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(TestRunStartOutcome.WorkspaceCreditCapReached, started.Outcome);
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_workspace_too_busy_to_start_a_run_starts_nothing()
    {
        _runStarter.Setup(x => x.StartTestAsync(5, Trigger.ToString(), It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RunStartBusyException(3));

        TestRunStart started = await Starter().StartAsync(5, Trigger, Payload, CancellationToken.None);

        Assert.Equal(TestRunStartOutcome.Busy, started.Outcome);
        _dispatcher.VerifyNoOtherCalls();
    }

    private TestRunStarter Starter() =>
        new(_runStarter.Object, _dispatcher.Object, _runs.Object, TimeProvider.System);
}
