using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Host.Tests.AuthHost;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// Publish, enable, disable, deprecate and reinstate as the service composes them. Which state changes are
/// legal is the procedures' decision (WorkflowStateIntegrationTests); here the providers stand in, and each
/// test pins what the service does with their answer: the response, the events, and what it never calls.
/// </summary>
public sealed class WorkflowLifecycleServiceTests
{
    private const int WorkspaceId = 1;
    private const int UserId = 7;

    [Fact]
    public async Task Publish_without_enable_adds_a_version_and_leaves_the_live_one_alone()
    {
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: false);
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 1));
        h.InsertReturns(id: 6, version: 2);
        h.States.Setup(x => x.FindAsync(request.RefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(request.RefId, WorkspaceId, WorkflowState.Enabled, 5, 1, 2));

        var response = await h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(new WorkflowPublishResponse(request.RefId, 2, WorkflowState.Enabled, 1), response.Value);
        h.States.Verify(x => x.EnableAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Bus.Verify(x => x.PublishAsync(It.Is<WorkflowPublishedEvent>(e => e.Version == 2 && !e.Enabled), It.IsAny<CancellationToken>()), Times.Once);

        // Nothing the engine caches moved.
        h.Bus.Verify(x => x.PublishAsync(It.IsAny<WorkflowDefinitionChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_with_enable_makes_the_new_version_live()
    {
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: true);
        var workspaceRef = Guid.NewGuid();
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 1));
        h.Definitions.Setup(x => x.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 1));
        h.InsertReturns(id: 6, version: 2);
        h.States.Setup(x => x.EnableAsync(request.RefId, WorkspaceId, 2, UserId, It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowEnableResult(true, 5, 6, 0));

        var response = await h.Service.PublishAsync(WorkspaceId, workspaceRef, request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(new WorkflowPublishResponse(request.RefId, 2, WorkflowState.Enabled, 2), response.Value);
        h.Bus.Verify(x => x.PublishAsync(It.Is<WorkflowPublishedEvent>(e => e.Version == 2 && e.Enabled), It.IsAny<CancellationToken>()), Times.Once);
        h.Bus.Verify(x => x.PublishAsync(It.Is<WorkflowEnabledEvent>(e => e.WorkflowId == 6 && e.Version == 2 && e.PreviousVersion == 1), It.IsAny<CancellationToken>()), Times.Once);
        h.Bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowDefinitionChangedEvent>(e => e.WorkflowRefId == request.RefId && e.DefinitionIds.Order().SequenceEqual(new[] { 5, 6 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_with_enable_that_is_refused_keeps_the_version_and_says_so()
    {
        // E4: the version is published and the previous live version keeps running. Nothing is undone;
        // the caller retries with Enable vN.
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: true);
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 2));
        h.InsertReturns(id: 6, version: 3);
        h.States.Setup(x => x.EnableAsync(request.RefId, WorkspaceId, 3, UserId, It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(WorkflowLifecycleErrors.IntegrationMissing, "gone"));

        var response = await h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsFailure);
        Assert.Equal("WORKFLOW_VALIDATION_FAILED", response.Error.Code);
        Assert.StartsWith("Version 3 was published but not made live:", response.Error.Message);
        h.Definitions.Verify(x => x.DeleteUnreferencedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Bus.Verify(x => x.PublishAsync(It.IsAny<WorkflowPublishedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Bus.Verify(x => x.PublishAsync(It.IsAny<WorkflowEnabledEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_stamps_the_resolved_workspace_and_takes_the_version_from_the_inserted_row()
    {
        // The procedure assigns the version under HOLDLOCK; a number derived from the earlier unlocked
        // read could disagree with it under concurrent publishes.
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: false);
        WorkflowDefinitionRow? submitted = null;
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 1, workspaceId: 42));
        h.Definitions.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<int?>(), It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, IReadOnlyCollection<WorkflowIntegrationRef> _, int? _, WorkflowPublishGuards _, CancellationToken _) => { submitted = row; return row with { Id = 6, Version = 9 }; });

        var response = await h.Service.PublishAsync(42, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(9, response.Value.Version);
        Assert.Equal(0, submitted!.Version);
        Assert.Equal(42, submitted.WorkspaceId);
    }

    [Fact]
    public async Task Publish_does_not_treat_a_database_error_as_a_missing_workflow()
    {
        // Swallowing the lookup's fault would let it read as a first publish and skip the ownership check.
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: true);
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("connection reset"));

        await Assert.ThrowsAsync<TimeoutException>(() => h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None));

        h.VerifyNothingInserted();
    }

    [Fact]
    public async Task Publish_over_another_workspaces_workflow_is_a_conflict()
    {
        var h = new Harness();
        WorkflowPublishRequest request = PublishRequest(enable: true);
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(5, request.RefId, 1));

        var response = await h.Service.PublishAsync(WorkspaceId + 99, Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal("WORKFLOW_REF_TAKEN", response.Error.Code);
        h.VerifyNothingInserted();
    }

    [Fact]
    public async Task Publish_of_an_invalid_definition_writes_nothing()
    {
        var h = new Harness();

        var response = await h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), new WorkflowPublishRequest(Guid.NewGuid(), "Invalid", null, null!, true), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, response.Error.Type);
        h.VerifyNothingInserted();
    }

    [Fact]
    public async Task A_new_workflow_past_the_workspace_ceiling_is_refused_with_the_configured_number()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Workflows:MaxPerWorkspace"] = "25" })
            .Build();
        var h = new Harness(configuration);
        WorkflowPublishRequest request = PublishRequest(enable: true);
        h.Definitions.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), 25, It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(WorkflowLifecycleErrors.LimitReached, "limit"));

        var response = await h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal("WORKFLOW_LIMIT_REACHED", response.Error.Code);
        Assert.Contains("25", response.Error.Message);
        h.VerifyNeverEnabled();
    }

    [Theory]
    [InlineData(WorkflowLifecycleErrors.IsDeprecated, "WORKFLOW_DEPRECATED")]
    [InlineData(WorkflowLifecycleErrors.Deleted, "WORKFLOW_DELETED")]
    [InlineData(WorkflowLifecycleErrors.IntegrationMissing, "WORKFLOW_VALIDATION_FAILED")]
    public async Task Publish_refused_by_the_procedure_answers_its_error(int number, string code)
    {
        var h = new Harness();
        h.Definitions.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<int?>(), It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(number, "refused"));

        var response = await h.Service.PublishAsync(WorkspaceId, Guid.NewGuid(), PublishRequest(enable: true), CancellationToken.None);

        Assert.Equal(code, response.Error.Code);
        h.VerifyNeverEnabled();
        h.Bus.Verify(x => x.PublishAsync(It.IsAny<WorkflowPublishedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enable_of_a_missing_version_of_a_visible_workflow_is_version_not_found()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.FindAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(refId, WorkspaceId, WorkflowState.Enabled, 5, 1, 2));

        var response = await h.Service.EnableAsync(WorkspaceId, Guid.NewGuid(), refId, 3, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, response.Error.Type);
        Assert.Equal("WORKFLOW_VERSION_NOT_FOUND", response.Error.Code);
        h.VerifyNeverEnabled();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enable_of_an_unknown_or_foreign_workflow_is_workflow_not_found(bool foreign)
    {
        // E7: the same answer for both, so a version number reveals nothing about another workspace.
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.Definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign ? Row(9, refId, 3, workspaceId: WorkspaceId + 1) : null);

        var response = await h.Service.EnableAsync(WorkspaceId, Guid.NewGuid(), refId, 3, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, response.Error.Type);
        Assert.Equal("WORKFLOW_NOT_FOUND", response.Error.Code);
        h.VerifyNeverEnabled();
    }

    [Fact]
    public async Task Enable_holds_an_old_version_to_todays_rules()
    {
        // E5: a version published before a rule existed would otherwise go live and fail every run.
        var h = new Harness();
        var refId = Guid.NewGuid();
        WorkflowDefinition definition = Fixture();
        WorkflowDefinition broken = definition with { Edges = [.. definition.Edges, definition.Edges.First()] };
        h.Definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(4, refId, 1) with { DefinitionJson = JsonSerializer.Serialize(broken, JsonOptions) });

        var response = await h.Service.EnableAsync(WorkspaceId, Guid.NewGuid(), refId, 1, CancellationToken.None);

        Assert.Equal("WORKFLOW_VALIDATION_FAILED", response.Error.Code);
        Assert.Contains("DUPLICATE_PORT_EDGE", response.Error.Message);
        h.VerifyNeverEnabled();
    }

    [Fact]
    public async Task Enable_plans_the_versions_triggers_under_the_workspace()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        var workspaceRef = Guid.NewGuid();
        TriggerPlan? planned = null;
        h.Definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>())).ReturnsAsync(Row(8, refId, 2));
        h.States.Setup(x => x.EnableAsync(refId, WorkspaceId, 2, UserId, It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, int _, int _, int _, TriggerPlan plan, IReadOnlyCollection<WorkflowIntegrationRef> _, CancellationToken _) => planned = plan)
            .ReturnsAsync(new WorkflowEnableResult(true, null, 8, 0));

        var response = await h.Service.EnableAsync(WorkspaceId, workspaceRef, refId, 2, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(new WorkflowStateResponse(refId, WorkflowState.Enabled, 2), response.Value);
        Assert.Equal(new TriggerPlanner(TriggerChannelRegistry.Default, TimeProvider.System).Plan(Fixture(), refId, workspaceRef).Registrations.Select(r => r.TriggerKey),
            planned!.Registrations.Select(r => r.TriggerKey));
        h.Bus.Verify(x => x.PublishAsync(It.Is<WorkflowEnabledEvent>(e => e.Version == 2 && e.PreviousVersion == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enabling_the_live_version_again_records_nothing()
    {
        // E6: a retried click is harmless.
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.Definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>())).ReturnsAsync(Row(8, refId, 2));
        h.States.Setup(x => x.EnableAsync(refId, WorkspaceId, 2, UserId, It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowEnableResult(false, 8, 8, 0));

        var response = await h.Service.EnableAsync(WorkspaceId, Guid.NewGuid(), refId, 2, CancellationToken.None);

        Assert.True(response.IsSuccess);
        h.Bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WorkflowLifecycleErrors.IsDeprecated, "WORKFLOW_DEPRECATED")]
    [InlineData(WorkflowLifecycleErrors.NotFound, "WORKFLOW_NOT_FOUND")]
    [InlineData(WorkflowLifecycleErrors.NoSuchVersion, "WORKFLOW_VERSION_NOT_FOUND")]
    public async Task Enable_refused_by_the_procedure_answers_its_error(int number, string code)
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.Definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>())).ReturnsAsync(Row(8, refId, 2));
        h.States.Setup(x => x.EnableAsync(refId, WorkspaceId, 2, UserId, It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(number, "refused"));

        var response = await h.Service.EnableAsync(WorkspaceId, Guid.NewGuid(), refId, 2, CancellationToken.None);

        Assert.Equal(code, response.Error.Code);
        h.Bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task Disable_cancels_the_runs_going_only_when_asked(bool cancelRunningRuns, int cancelled)
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.DisableAsync(refId, WorkspaceId, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDisableResult(true, 8, 3, [101, 102]));
        h.Definitions.Setup(x => x.GetByIdAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(Row(8, refId, 4));

        var response = await h.Service.DisableAsync(WorkspaceId, refId, cancelRunningRuns, CancellationToken.None);

        Assert.Equal(new WorkflowStateResponse(refId, WorkflowState.Disabled, null, cancelled, 3), response.Value);
        h.Engine.Verify(x => x.QueueCancelRunAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(cancelled));
        h.Bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowDisabledEvent>(e => e.WorkflowId == 8 && e.Version == 4 && e.CancelledRuns == cancelled && e.DroppedQueuedEvents == 3),
            It.IsAny<CancellationToken>()), Times.Once);
        h.Bus.Verify(x => x.PublishAsync(It.Is<WorkflowDefinitionChangedEvent>(e => e.DefinitionIds.SequenceEqual(new[] { 8 })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disabling_a_disabled_workflow_records_nothing()
    {
        // E6 for disable: one audit entry however often it is clicked.
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.DisableAsync(refId, WorkspaceId, UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDisableResult(false, null, 0, []));

        var response = await h.Service.DisableAsync(WorkspaceId, refId, false, CancellationToken.None);

        Assert.Equal(WorkflowState.Disabled, response.Value.State);
        h.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Disabling_a_deprecated_workflow_is_a_conflict()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.DisableAsync(refId, WorkspaceId, UserId, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(WorkflowLifecycleErrors.IsDeprecated, "deprecated"));

        var response = await h.Service.DisableAsync(WorkspaceId, refId, true, CancellationToken.None);

        Assert.Equal("WORKFLOW_DEPRECATED", response.Error.Code);
        h.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Deprecate_records_the_state_it_came_from_and_leaves_runs_alone()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.FindAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(refId, WorkspaceId, WorkflowState.Enabled, 8, 4, 6));
        h.States.Setup(x => x.DisableAsync(refId, WorkspaceId, UserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDisableResult(true, 8, 0, [101]));
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(10, refId, 6));

        var response = await h.Service.DeprecateAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal(WorkflowState.Deprecated, response.Value.State);
        h.Engine.VerifyNoOtherCalls();
        h.Bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowDeprecatedEvent>(e => e.WorkflowId == 10 && e.Version == 6 && e.Changes!.Single().Before == "Enabled" && e.Changes!.Single().After == "Deprecated"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reinstate_brings_a_deprecated_workflow_back_disabled()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.Definitions.Setup(x => x.FindCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(10, refId, 6));

        var response = await h.Service.ReinstateAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal(new WorkflowStateResponse(refId, WorkflowState.Disabled, null), response.Value);
        h.States.Verify(x => x.ReinstateAsync(refId, WorkspaceId, UserId, It.IsAny<CancellationToken>()), Times.Once);
        h.Bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowReinstatedEvent>(e => e.Version == 6 && e.Changes!.Single().After == "Disabled"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reinstating_a_workflow_that_is_not_deprecated_is_a_conflict()
    {
        var h = new Harness();
        var refId = Guid.NewGuid();
        h.States.Setup(x => x.ReinstateAsync(refId, WorkspaceId, UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(WorkflowLifecycleErrors.NotDeprecated, "not deprecated"));

        var response = await h.Service.ReinstateAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal("WORKFLOW_NOT_DEPRECATED", response.Error.Code);
        h.Bus.VerifyNoOtherCalls();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string FixtureJson()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json"));
    }

    private static WorkflowDefinition Fixture() => JsonSerializer.Deserialize<WorkflowDefinition>(FixtureJson(), JsonOptions)!;

    private static WorkflowPublishRequest PublishRequest(bool enable)
    {
        WorkflowDefinition definition = Fixture();
        return new WorkflowPublishRequest(definition.WorkflowRefId, "Vent control + escalation", null, definition, enable);
    }

    private static WorkflowDefinitionRow Row(int id, Guid refId, int version, int workspaceId = WorkspaceId) => new()
    {
        Id = id,
        RefId = refId,
        Version = version,
        WorkspaceId = workspaceId,
        Name = "Vent control + escalation",
        Description = null,
        DefinitionJson = FixtureJson(),
        PublishedBy = UserId,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class Harness
    {
        public Mock<IWorkflowDefinitionProvider> Definitions { get; } = new();
        public Mock<IWorkflowStateProvider> States { get; } = new();
        public Mock<IWorkflowEngineGateway> Engine { get; } = new();
        public Mock<IEventBus> Bus { get; } = new();
        public WorkflowLifecycleService Service { get; }

        public Harness(IConfiguration? configuration = null)
        {
            var identity = new Mock<IIdentityService>();
            identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(UserId));
            Service = new WorkflowLifecycleService(
                Definitions.Object,
                States.Object,
                new TriggerPlanner(TriggerChannelRegistry.Default, TimeProvider.System),
                new WorkflowValidator(),
                new IntegrationReferenceCheck(Mock.Of<IIntegrationProvider>()),
                identity.Object,
                Engine.Object,
                Bus.Object,
                Mock.Of<ILogger<WorkflowLifecycleService>>(),
                configuration);
        }

        public void InsertReturns(int id, int version)
        {
            Definitions.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<int?>(), It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkflowDefinitionRow row, IReadOnlyCollection<WorkflowIntegrationRef> _, int? _, WorkflowPublishGuards _, CancellationToken _) => row with { Id = id, Version = version });
        }

        public void VerifyNothingInserted() =>
            Definitions.Verify(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<int?>(), It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()), Times.Never);

        public void VerifyNeverEnabled() =>
            States.Verify(x => x.EnableAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TriggerPlan>(), It.IsAny<IReadOnlyCollection<WorkflowIntegrationRef>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
