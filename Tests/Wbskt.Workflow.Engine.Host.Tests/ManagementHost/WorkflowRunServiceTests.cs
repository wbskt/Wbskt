using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Manual runs, signals and cancels, tested without a controller: every engine outcome has one
/// answer, and only the outcomes that started something are announced.
/// </summary>
public sealed class WorkflowRunServiceTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkflowRef = Guid.NewGuid();

    private readonly Mock<IWorkflowQueryService> _definitions = new();
    private readonly Mock<IWorkflowRunQueryService> _runs = new();
    private readonly Mock<IWorkflowEngineGateway> _engine = new();
    private readonly Mock<IEventBus> _bus = new();

    private WorkflowRunService Service => new(_definitions.Object, _runs.Object, _engine.Object, _bus.Object, NullLogger<WorkflowRunService>.Instance);

    private void WorkflowIs(string status)
    {
        _definitions.Setup(d => d.GetCurrentAsync(WorkspaceId, WorkflowRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowDefinitionDto>.Success(new WorkflowDefinitionDto(WorkflowRef, 1, status, "wf", null, null!, DateTime.UtcNow)));
    }

    private void EngineAnswers(StartRunOutcome outcome)
    {
        _engine.Setup(e => e.StartManualRunAsync(WorkflowRef, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartRunResponse(Guid.NewGuid(), 1, outcome));
    }

    [Theory]
    [InlineData(StartRunOutcome.Started)]
    [InlineData(StartRunOutcome.Queued)]
    [InlineData(StartRunOutcome.Duplicate)]
    public async Task A_run_the_engine_took_succeeds_and_is_announced(StartRunOutcome outcome)
    {
        WorkflowIs("Published");
        EngineAnswers(outcome);

        var result = await Service.StartManualAsync(WorkspaceId, WorkflowRef, new StartRunRequest("manual", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(outcome);
        _bus.Verify(b => b.PublishAsync(It.Is<WorkflowRunRequestedEvent>(e => e.Outcome == outcome.ToString()), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(StartRunOutcome.Dropped, "RUN_DROPPED_BY_CONCURRENCY_POLICY")]
    [InlineData(StartRunOutcome.NoManualTrigger, "WORKFLOW_HAS_NO_MANUAL_TRIGGER")]
    public async Task A_run_the_engine_refused_is_a_409_and_announces_nothing(StartRunOutcome outcome, string code)
    {
        WorkflowIs("Published");
        EngineAnswers(outcome);

        var result = await Service.StartManualAsync(WorkspaceId, WorkflowRef, new StartRunRequest("manual", null), CancellationToken.None);

        result.Error.Code.Should().Be(code);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_deprecated_workflow_is_a_409_and_the_engine_is_not_asked()
    {
        WorkflowIs("Deprecated");

        var result = await Service.StartManualAsync(WorkspaceId, WorkflowRef, new StartRunRequest("manual", null), CancellationToken.None);

        result.Error.Code.Should().Be("WORKFLOW_DEPRECATED");
        _engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_engine_that_fails_is_a_fault_not_a_result()
    {
        WorkflowIs("Published");
        _engine.Setup(e => e.StartManualRunAsync(WorkflowRef, It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("engine down"));

        var start = () => Service.StartManualAsync(WorkspaceId, WorkflowRef, new StartRunRequest("manual", null), CancellationToken.None);

        await start.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_cancel_with_the_broker_down_is_unavailable_and_announces_nothing()
    {
        var runRef = Guid.NewGuid();
        _runs.Setup(r => r.ResolveCancellableRunAsync(WorkspaceId, runRef, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(66));
        _engine.Setup(e => e.CancelRunAsync(66, "stop", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Service.CancelAsync(WorkspaceId, runRef, "stop", CancellationToken.None);

        result.Error.Should().Be(WorkflowRunService.BrokerUnavailable);
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_signal_to_another_workspaces_run_never_reaches_the_engine()
    {
        var runRef = Guid.NewGuid();
        _runs.Setup(r => r.EnsureRunInWorkspaceAsync(WorkspaceId, runRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.NotFound("RUN_NOT_FOUND", "Run not found.")));

        var result = await Service.SignalAsync(WorkspaceId, runRef, "go", new SignalRequest("go", default), CancellationToken.None);

        result.Error.Code.Should().Be("RUN_NOT_FOUND");
        _engine.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }
}
