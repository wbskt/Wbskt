using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkflowTestRunServiceTests
{
    private const int WorkspaceId = 7;
    private const int CopyId = 900;
    private static readonly Guid RefId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid TriggerA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TriggerB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid RunRef = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly Mock<IWorkflowDefinitionProvider> _definitions = new();
    private readonly Mock<IWorkflowDraftProvider> _drafts = new();
    private readonly Mock<IWorkflowQueryService> _query = new();
    private readonly Mock<IWorkflowEngineGateway> _engine = new();

    public WorkflowTestRunServiceTests()
    {
        _query.Setup(x => x.ValidateAsync(WorkspaceId, It.IsAny<WorkflowDefinition?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowValidationResponse(true, []));
        _definitions.Setup(x => x.InsertTestCopyAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = CopyId });
        _engine.Setup(x => x.StartTestRunAsync(It.IsAny<EngineTestRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EngineTestRunResponse(EngineTestRunOutcome.Started, RunRef));
    }

    [Fact]
    public async Task A_draft_runs_from_a_copy_based_on_its_version()
    {
        Draft(baseVersion: 4, TriggerA);
        var payload = new Dictionary<string, JsonElement> { ["body"] = JsonSerializer.SerializeToElement(new { on = true }) };

        var result = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft, payload), CancellationToken.None);

        Assert.Equal(RunRef, result.Value.RunRefId);
        _definitions.Verify(x => x.InsertTestCopyAsync(It.Is<WorkflowDefinitionRow>(r => r.RefId == RefId && r.Version == 4 && r.WorkspaceId == WorkspaceId), It.IsAny<CancellationToken>()), Times.Once);
        _engine.Verify(x => x.StartTestRunAsync(It.Is<EngineTestRunRequest>(r => r.WorkflowDefinitionId == CopyId && r.TriggerNodeId == TriggerA && r.Payload.ContainsKey("body")), It.IsAny<CancellationToken>()), Times.Once);
        _definitions.Verify(x => x.DeleteUnreferencedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_live_version_runs_from_a_copy_of_itself()
    {
        _definitions.Setup(x => x.FindCurrentByRefIdAsync(RefId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowDefinitionRow
        {
            Id = 3, RefId = RefId, Version = 6, WorkspaceId = WorkspaceId, Name = "Live", Description = null, IsEnabled = true,
            DefinitionJson = Json(TriggerA), PublishedBy = 1, CreatedAt = DateTime.UtcNow
        });

        var result = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Live), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _definitions.Verify(x => x.InsertTestCopyAsync(It.Is<WorkflowDefinitionRow>(r => r.Version == 6 && r.Name == "Live"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task With_two_triggers_the_caller_names_one()
    {
        Draft(baseVersion: null, TriggerA, TriggerB);

        var unnamed = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft), CancellationToken.None);
        Assert.Equal("TEST_RUN_TRIGGER_REQUIRED", unnamed.Error.Code);

        var wrong = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft, TriggerNodeId: Guid.NewGuid()), CancellationToken.None);
        Assert.Equal("TEST_RUN_TRIGGER_NOT_FOUND", wrong.Error.Code);

        var named = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft, TriggerNodeId: TriggerB), CancellationToken.None);
        Assert.True(named.IsSuccess);
        _definitions.Verify(x => x.InsertTestCopyAsync(It.Is<WorkflowDefinitionRow>(r => r.Version == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_invalid_draft_is_not_run()
    {
        Draft(baseVersion: 1, TriggerA);
        _query.Setup(x => x.ValidateAsync(WorkspaceId, It.IsAny<WorkflowDefinition?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowValidationResponse(false, [new WorkflowValidationIssueDto("Error", "NO_END", "No end.", null)]));

        var result = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft), CancellationToken.None);

        Assert.Equal("WORKFLOW_VALIDATION_FAILED", result.Error.Code);
        _definitions.Verify(x => x.InsertTestCopyAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_second_test_run_is_refused_naming_the_first_and_its_copy_is_removed()
    {
        Draft(baseVersion: 1, TriggerA);
        Guid active = Guid.NewGuid();
        _engine.Setup(x => x.StartTestRunAsync(It.IsAny<EngineTestRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EngineTestRunResponse(EngineTestRunOutcome.TestRunActive, ActiveRunRefId: active));

        var result = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft), CancellationToken.None);

        Assert.Equal("TEST_RUN_ACTIVE", result.Error.Code);
        Assert.Contains(active.ToString(), result.Error.Message);
        _definitions.Verify(x => x.DeleteUnreferencedAsync(CopyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task When_the_engine_cannot_be_asked_the_copy_is_removed()
    {
        Draft(baseVersion: 1, TriggerA);
        _engine.Setup(x => x.StartTestRunAsync(It.IsAny<EngineTestRunRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("engine down"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft), CancellationToken.None));

        _definitions.Verify(x => x.DeleteUnreferencedAsync(CopyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_a_draft_there_is_nothing_to_run()
    {
        var result = await Service().StartAsync(WorkspaceId, RefId, new StartTestRunRequest(TestRunSource.Draft), CancellationToken.None);

        Assert.Equal("WORKFLOW_DRAFT_NOT_FOUND", result.Error.Code);
    }

    private WorkflowTestRunService Service()
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(x => x.GetUserIdentity()).Returns(new UserIdentity(9));
        return new WorkflowTestRunService(_definitions.Object, _drafts.Object, _query.Object, _engine.Object, identity.Object, Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowTestRunService>>());
    }

    private void Draft(int? baseVersion, params Guid[] triggers)
    {
        _drafts.Setup(x => x.FindAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowDraftRow
        {
            RefId = RefId, WorkspaceId = WorkspaceId, BaseVersion = baseVersion, Revision = 1, Name = "Draft", Description = null,
            DefinitionJson = Json(triggers), CreatedBy = 9, CreatedAt = DateTime.UtcNow, UpdatedBy = 9, UpdatedByRefId = null,
            UpdatedAt = DateTime.UtcNow, CurrentVersion = baseVersion
        });
    }

    private static string Json(params Guid[] triggers)
    {
        var definition = new WorkflowDefinition(
            RefId, 0, WorkspaceId, "wf", null, true,
            triggers.Select(id => (BaseNode)new ManualTriggerNode
            {
                NodeId = id,
                Name = "run",
                Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
                Config = new ManualTriggerConfig { Description = "run" }
            }).ToList(),
            [], [], DateTime.UtcNow, 0);
        return JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
