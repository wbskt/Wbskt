using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkflowQueryServiceTests
{
    private const int WorkspaceId = 1;

    private readonly Mock<IWorkflowDefinitionProvider> _definitions = new();
    private readonly Mock<IWorkflowStateProvider> _states = new();

    [Fact]
    public async Task Validate_reports_issues_without_reading_or_writing_anything()
    {
        var empty = LoadDefinition() with { Nodes = [], Edges = [] };

        WorkflowValidationResponse response = await CreateService().ValidateAsync(WorkspaceId, empty, CancellationToken.None);

        Assert.True(response.IsValid, "an empty definition has warnings but no errors");
        Assert.Contains(response.Issues, issue => issue.Code == "NO_TRIGGERS" && issue.Severity == "Warning");
        _definitions.VerifyNoOtherCalls();
        _states.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Validate_returns_errors_with_the_offending_node()
    {
        var definition = LoadDefinition();
        var duplicateEdge = definition.Edges.First();

        WorkflowValidationResponse response = await CreateService().ValidateAsync(WorkspaceId, definition with { Edges = [.. definition.Edges, duplicateEdge] }, CancellationToken.None);

        Assert.False(response.IsValid);
        var issue = Assert.Single(response.Issues, i => i.Code == "DUPLICATE_PORT_EDGE");
        Assert.Equal("Error", issue.Severity);
        Assert.NotNull(issue.NodeId);
    }

    [Fact]
    public async Task GetCurrent_carries_the_workflows_state_and_live_version()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.FindCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(12, refId, 4));
        _states.Setup(x => x.FindAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(refId, WorkspaceId, WorkflowState.Enabled, 10, 3, 4));

        var result = await CreateService().GetCurrentAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(refId, result.Value.RefId);
        Assert.Equal(4, result.Value.Version);
        Assert.Equal(WorkflowState.Enabled, result.Value.State);
        Assert.Equal(3, result.Value.LiveVersion);
        Assert.Equal("Vent control + escalation", result.Value.Name);
    }

    [Fact]
    public async Task GetCurrent_of_a_disabled_workflow_has_no_live_version()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.FindCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(12, refId, 2));
        _states.Setup(x => x.FindAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(refId, WorkspaceId, WorkflowState.Disabled, null, null, 2));

        var result = await CreateService().GetCurrentAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal(WorkflowState.Disabled, result.Value.State);
        Assert.Null(result.Value.LiveVersion);
    }

    [Fact]
    public async Task GetCurrent_in_another_workspace_is_not_found()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.FindCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(12, refId, 4));

        var result = await CreateService().GetCurrentAsync(WorkspaceId + 1, refId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
        _states.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetVersion_returns_that_version_with_the_workflows_state()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.FindRowByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>())).ReturnsAsync(Row(13, refId, 2));
        _states.Setup(x => x.FindAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowStateRow(refId, WorkspaceId, WorkflowState.Enabled, 14, 3, 3));

        var result = await CreateService().GetVersionAsync(WorkspaceId, refId, 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal(3, result.Value.LiveVersion);
    }

    [Fact]
    public async Task GetVersions_marks_only_the_live_version()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.GetVersionsAsync(refId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Version(2, isLive: false),
                Version(1, isLive: true),
            ]);

        var result = await CreateService().GetVersionsAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal([false, true], result.Value.Select(v => v.IsLive));
    }

    [Fact]
    public async Task Versions_of_a_workflow_that_is_not_here_are_not_found()
    {
        var refId = Guid.NewGuid();
        _definitions.Setup(x => x.GetVersionsAsync(refId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateService().GetVersionsAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
    }

    private static WorkflowVersionRow Version(int version, bool isLive) => new()
    {
        Version = version,
        Name = "wf",
        IsLive = isLive,
        PublishedBy = 7,
        CreatedAt = DateTime.UtcNow,
        RunCount = version * 10,
    };

    private WorkflowQueryService CreateService() => new(
        _definitions.Object,
        _states.Object,
        new WorkflowValidator(),
        new IntegrationReferenceCheck(Mock.Of<IIntegrationProvider>()),
        Mock.Of<ILogger<WorkflowQueryService>>());

    private static string LoadFixture()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json"));
    }

    private static WorkflowDefinition LoadDefinition() =>
        JsonSerializer.Deserialize<WorkflowDefinition>(LoadFixture(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static WorkflowDefinitionRow Row(int id, Guid refId, int version) => new()
    {
        Id = id,
        RefId = refId,
        Version = version,
        WorkspaceId = WorkspaceId,
        Name = "Vent control + escalation",
        Description = "Initial setup",
        DefinitionJson = LoadFixture(),
        PublishedBy = 7,
        CreatedAt = DateTime.UtcNow,
    };
}
