using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

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

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();

        // Publish a trivial manual workflow (trigger only → starts and completes).
        var workflowRefId = Guid.NewGuid();
        var definition = BuildManualDefinition(workflowRefId, out var triggerNodeId);

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Idem-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // Start the run twice with the SAME idempotency key.
        var idempotencyKey = $"e2e-idem-{Guid.NewGuid():N}";
        var first = await fixture.StartManualRunAsync(token, workspaceRef, publishedRef, triggerNodeId.ToString(), idempotencyKey);
        var second = await fixture.StartManualRunAsync(token, workspaceRef, publishedRef, triggerNodeId.ToString(), idempotencyKey);

        // A manual start is answered once the engine has dispatched it, so the answers are the signal
        // to wait for: the first started the run, and the second was recognised as a repeat of it
        // (reported as a duplicate, or as the original's own start replayed).
        first.Outcome.Should().Be(StartRunOutcome.Started);
        (second.Outcome == StartRunOutcome.Duplicate
                || (second.Outcome == StartRunOutcome.Started && second.RunRefId == first.RunRefId))
            .Should().BeTrue(
                $"the second start with the same idempotency key must be deduped (outcome {second.Outcome}, run {second.RunRefId}, first run {first.RunRefId})");
        if (second.RunRefId != Guid.Empty)
        {
            second.RunRefId.Should().Be(first.RunRefId, "a deduplicated start answers with the run the original started");
        }

        var runs = await fixture.WaitForRunCountAsync(token, workspaceRef, publishedRef, 1, TimeSpan.FromSeconds(30));
        runs.Should().ContainSingle(
            "the second start with the same idempotency key must be deduped — only one run may exist")
            .Which.RefId.Should().Be(first.RunRefId);
    }

    private static WorkflowDefinition BuildManualDefinition(Guid workflowRefId, out Guid triggerNodeId)
    {
        var builder = new WorkflowBuilder($"E2E-Idem-{workflowRefId:N}", workflowRefId)
            .AddManualTrigger(out triggerNodeId);
        
        return builder.BuildAndValidate();
    }
}
