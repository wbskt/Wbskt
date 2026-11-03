using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>The workflow list says what starts each workflow (#199), so the explorer needs no definition per row.</summary>
public sealed class WorkflowListTriggerKindsTests
{
    [Fact]
    public async Task Each_workflow_lists_its_trigger_kinds_by_channel_name_in_node_order()
    {
        var provider = new Mock<IWorkflowDefinitionProvider>();
        provider.Setup(p => p.GetAllSummariesAsync(1, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<WorkflowDefinitionRow>(
            [
                Row("Doorbell", ["trigger:client", "trigger:manual", "trigger:clientPresence"]),
                Row("Nightly", ["trigger:schedule", "trigger:webhook"]),
                Row("Empty", [])
            ], 3));
        var queries = new WorkflowQueryService(provider.Object, new WorkflowValidator(), new IntegrationReferenceCheck(Mock.Of<IIntegrationProvider>()), Mock.Of<ILogger<WorkflowQueryService>>());

        var page = (await queries.GetAllSummariesAsync(1, 0, 100, CancellationToken.None)).Value;

        Assert.Equal(["client", "manual", "presence"], page.Single(w => w.Name == "Doorbell").TriggerKinds);
        Assert.Equal(["schedule", "webhook"], page.Single(w => w.Name == "Nightly").TriggerKinds);
        Assert.Empty(page.Single(w => w.Name == "Empty").TriggerKinds!);
    }

    [Fact]
    public void A_trigger_kind_this_host_does_not_know_still_reads_as_itself() =>
        Assert.Equal(["client", "telegram"], WorkflowTriggerKinds.FromNodeKinds(["trigger:client", "trigger:telegram", "trigger:client"]));

    private static WorkflowDefinitionRow Row(string name, IReadOnlyList<string> kinds) => new()
    {
        Id = 1,
        RefId = Guid.NewGuid(),
        Version = 1,
        WorkspaceId = 1,
        Name = name,
        Description = null,
        IsEnabled = true,
        DefinitionJson = "{}",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow,
        TriggerNodeKinds = kinds
    };
}
