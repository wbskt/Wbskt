using System.Text.Json;
using Moq;
using Wbskt.Host.Tests.Abstraction;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>#940: a "Run workflow" step may only name one of the workspace's own workflows.</summary>
public sealed class SubWorkflowReferenceCheckTests
{
    private const int WorkspaceId = 1;
    private static readonly Guid ChildRef = Guid.NewGuid();

    private readonly Mock<IWorkflowDefinitionProvider> _definitions = new();

    [Fact]
    public async Task A_workflow_the_workspace_does_not_have_is_an_error_on_the_ref_field()
    {
        var issue = Assert.Single(await CheckAsync());

        Assert.Equal("SUBWORKFLOW_TARGET_NOT_FOUND", issue.Code);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("config.workflowRefId", issue.Field);
    }

    [Fact]
    public async Task Another_workspaces_workflow_reads_as_missing()
    {
        Child(WorkspaceId + 1, withManualTrigger: true);

        var issue = Assert.Single(await CheckAsync());

        Assert.Equal("SUBWORKFLOW_TARGET_NOT_FOUND", issue.Code);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
    }

    [Fact]
    public async Task A_workflow_without_a_manual_trigger_is_a_warning()
    {
        Child(WorkspaceId, withManualTrigger: false);

        var issue = Assert.Single(await CheckAsync());

        Assert.Equal("SUBWORKFLOW_TARGET_NOT_MANUAL", issue.Code);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task A_manual_workflow_of_the_workspace_is_fine()
    {
        Child(WorkspaceId, withManualTrigger: true);

        Assert.Empty(await CheckAsync());
    }

    [Fact]
    public async Task A_definition_without_a_run_workflow_step_reads_nothing()
    {
        var issues = await new SubWorkflowReferenceCheck(_definitions.Object).CheckAsync(WorkspaceId, ValidWorkflowBuilder.Build(), CancellationToken.None);

        Assert.Empty(issues);
        _definitions.VerifyNoOtherCalls();
    }

    private void Child(int workspaceId, bool withManualTrigger)
    {
        WorkflowDefinition child = ValidWorkflowBuilder.Build();
        if (withManualTrigger)
        {
            var manual = new ManualTriggerNode
            {
                NodeId = Guid.NewGuid(),
                Name = "run",
                Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
                Config = new ManualTriggerConfig { Description = "run" }
            };
            child = child with { Nodes = [.. child.Nodes, manual] };
        }

        _definitions.Setup(d => d.FindCurrentByRefIdAsync(ChildRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionRow
            {
                Id = 9,
                RefId = ChildRef,
                Version = 1,
                WorkspaceId = workspaceId,
                Name = "child",
                Description = null,
                DefinitionJson = JsonSerializer.Serialize(child, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = 1,
                CreatedAt = DateTime.UtcNow
            });
    }

    private Task<IReadOnlyList<ValidationIssue>> CheckAsync()
    {
        var step = new SubWorkflowNode
        {
            NodeId = Guid.NewGuid(),
            Name = "Run child",
            Ports = [],
            Config = new SubWorkflowConfig { WorkflowRefId = ChildRef }
        };
        WorkflowDefinition def = ValidWorkflowBuilder.Build();
        return new SubWorkflowReferenceCheck(_definitions.Object).CheckAsync(WorkspaceId, def with { Nodes = [.. def.Nodes, step] }, CancellationToken.None);
    }
}
