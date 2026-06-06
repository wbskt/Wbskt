using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests;

/// <summary>
/// Inbound idempotency — proves that re-POSTing a manual run-start with the same idempotency
/// key dedupes to a single run (the standard "idempotent POST" pattern).
///
/// Workflow: a single ManualTrigger node (the run starts and completes immediately). We call
/// StartManualRun twice with the SAME idempotency key. The first call starts a run; the second
/// is recognised as a duplicate at MatchInbound (the idempotency key already claimed) and is
/// suppressed. We assert exactly ONE run exists for the workflow.
///
/// Why this is the cleanest idempotency E2E: transport redeliveries and crash recovery can't be
/// forced from a black-box client, but a caller-supplied idempotency key is fully controllable.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class IdempotentManualStartE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TwoManualStarts_SameIdempotencyKey_ProduceExactlyOneRun()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();

        // Publish a trivial manual workflow (trigger only → starts and completes).
        var workflowRefId = Guid.NewGuid();
        var triggerNodeId = Guid.NewGuid();
        var definition = BuildManualDefinition(workflowRefId, triggerNodeId);

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Idem-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // Start the run twice with the SAME idempotency key.
        var idempotencyKey = $"e2e-idem-{Guid.NewGuid():N}";
        await fixture.StartManualRunAsync(token, workspaceRef, publishedRef, triggerNodeId.ToString(), idempotencyKey);
        await fixture.StartManualRunAsync(token, workspaceRef, publishedRef, triggerNodeId.ToString(), idempotencyKey);

        // Give the engine a moment to process, then assert exactly one run exists.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
        runs.Should().HaveCount(1,
            "the second start with the same idempotency key must be deduped — only one run may exist");
    }

    private static WorkflowDefinition BuildManualDefinition(Guid workflowRefId, Guid triggerNodeId)
    {
        var triggerNode = new ManualTriggerNode(
            NodeId: triggerNodeId,
            Name: "Manual Trigger",
            Ports: [new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new ManualTriggerConfig("e2e idempotency"));

        return new WorkflowDefinition(
            WorkflowRefId: workflowRefId,
            Version: 1,
            WorkspaceId: 1,
            Name: $"E2E-Idem-{workflowRefId:N}",
            Description: null,
            IsEnabled: true,
            Nodes: [triggerNode],
            Edges: [],
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1);
    }
}
