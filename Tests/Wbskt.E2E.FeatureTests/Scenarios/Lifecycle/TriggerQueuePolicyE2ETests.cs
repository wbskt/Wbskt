using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// Phase 1.3 — Queue concurrency policy must serialize same-correlation events one run at a time
/// via the PendingTriggerEvents queue, not drop them. Proves the PendingTriggerEventDrainer fix:
/// a single dequeue per run-terminal, with a re-minted InboundEventId so the drained event isn't
/// treated as a duplicate of the original (already-Succeeded) idempotency claim.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class TriggerQueuePolicyE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(3);

    [SkippableFact]
    public async Task Queue_SerializesConcurrentEventsWithSameCorrelation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceQueuePolicy1");

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceQueuePolicy1",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-Concurrency-Queue-{workflowRefId:N}", workflowRefId)
            // Queue policy: same-correlation events must run one-at-a-time, not be dropped.
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.Queue, "trigger.clientRefId", out var triggerNodeId)
            .AddAwaitSignal("continue", AwaitTimeout, awaitSignal =>
            {
                awaitSignal.OnSuccess(b => b.AddFailRun("Should not have received signal"));
                awaitSignal.OnTimeout(b => b.AddClientMessage(deviceRefStr, "RunDone", "Run finished", null));
            });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // ── Fire 3 events with the same correlation in rapid succession ──────
        foreach (int i in Enumerable.Range(0, 3))
        {
            await wbsktClient.SendAsync("telemetry", new { });
            await Task.Delay(50);
        }

        // ── Shortly after, exactly one run should have started; the other two
        //    events must be queued (PendingTriggerEvents), not dropped or run in parallel ──
        await Task.Delay(500);
        var runsShortlyAfter = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
        runsShortlyAfter.Should().HaveCount(1, "Queue policy must start only one run immediately and hold the rest in the pending-trigger queue");
        runsShortlyAfter[0].Status.Should().Be("Running");

        // ── Wait for all 3 to eventually run (serially) and finish ───────────
        var allDone = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
                return runs.Count == 3 && runs.All(r => IsTerminal(r.Status));
            },
            timeout: TimeSpan.FromSeconds(45),
            interval: TimeSpan.FromSeconds(2));

        var finalRuns = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
        allDone.Should().BeTrue(
            $"all 3 queued events must eventually each start and finish their own run. " +
            $"Runs seen: [{string.Join(", ", finalRuns.Select(r => $"RefId={r.RefId} Status={r.Status}"))}]");

        finalRuns.Should().HaveCount(3);
        finalRuns.Should().OnlyContain(r => r.Status == "Succeeded");

        // ── Serialization proof: no two runs overlapped in time ──────────────
        var ordered = finalRuns.OrderBy(r => r.StartedAt).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            ordered[i - 1].CompletedAt.Should().NotBeNull();
            ordered[i].StartedAt.Should().BeOnOrAfter(ordered[i - 1].CompletedAt!.Value.AddMilliseconds(-250),
                "queued runs must be serialized — each one starts only after the previous one finalizes");
        }
    }

    private static bool IsTerminal(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";
}
