using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Performance;

/// <summary>
/// A thousand webhook-started runs, each forking five ways and joining, all at once: the engine must
/// neither deadlock nor lose a run. Opt-in (<c>E2E_STRESS_TESTS=1</c>): it takes minutes and needs the
/// public callback rate limits and the engine's workspace run ceiling raised, so it runs nightly
/// (.github/workflows/nightly-stress.yml), not on the pull-request gate.
/// </summary>
[Collection(EngineCollection.Name)]
public class ConcurrencyStressE2ETests
{
    private readonly ServicesFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ConcurrencyStressE2ETests(ServicesFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task MassiveConcurrency_DoesNotDeadlock_AndAllRunsSucceed()
    {
        Skip.IfNot(_fixture.HostsAvailable && E2EConfig.StressTestsEnabled,
            "Fires a thousand webhooks and waits minutes for the runs. Set E2E_STRESS_TESTS=1 (with the hosts running) to run it.");

        // 1. A workspace of its own
        var (token, workspaceRef) = await _fixture.CreateIsolatedWorkspaceAsync();

        // 2. Define and Publish the Stress Workflow
        // Fork into 5 branches -> Delay 1s -> Join
        Guid workflowRefId = Guid.NewGuid();
        var builder = new WorkflowBuilder($"E2E-Stress-{workflowRefId:N}", workflowRefId)
            .AddWebhookTrigger("stress", "POST", WorkflowConcurrencyPolicy.AllowParallel, out _);

        builder.AddFork(["b1", "b2", "b3", "b4", "b5"], fork =>
        {
            for (int i = 0; i < 5; i++)
            {
                fork.Branch($"b{i + 1}", b => 
                {
                    b.AddDelay(TimeSpan.FromSeconds(1));
                });
            }
        }, JoinMode.All);

        // 3. Publish
        var definitionElement = JsonSerializer.SerializeToElement(builder.BuildAndValidate(), JsonOptions);
        await _fixture.PublishWorkflowAsync(token, workspaceRef, workflowRefId, "StressTest", definitionElement);

        // Wait for the trigger to be live, so none of the thousand lands before it. The warm-up can
        // start a run or two of its own, so the count below is a floor, not an exact figure.
        await _fixture.FireWebhookUntilRunAsync(token, workspaceRef, workflowRefId, "stress", TimeSpan.FromSeconds(60), new { warmup = true });

        // 4. Fire 1,000 webhooks concurrently (100 tasks * 10 webhooks)
        int numTasks = 100;
        int webhooksPerTask = 10;
        int totalRunsExpected = numTasks * webhooksPerTask;

        var tasks = new List<Task>();

        for (int i = 0; i < numTasks; i++)
        {
            int taskIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < webhooksPerTask; j++)
                {
                    var payload = new { task = taskIndex, iter = j };
                    await _fixture.SendWebhookAsync(workspaceRef, "stress", payload);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // 6. Wait for all 1,000 runs to finalize
        // It takes 1s for the delay + overhead. Give it up to 120 seconds to process 1,000 runs.
        bool allSucceeded = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await _fixture.ListRunsAsync(token, workspaceRef, workflowRefId, top: 1500);
                return runs.Count >= totalRunsExpected && runs.All(r => r.Status == "Succeeded");
            },
            timeout: TimeSpan.FromSeconds(500),
            interval: TimeSpan.FromSeconds(5)
        );

        var finalRuns = await _fixture.ListRunsAsync(token, workspaceRef, workflowRefId, top: 1500);
        var statusCounts = finalRuns.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        string counts = string.Join(", ", statusCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

        if (!allSucceeded)
        {
            var firstStuckRun = finalRuns.FirstOrDefault(r => r.Status != "Succeeded");
            if (firstStuckRun != null)
            {
                var history = await _fixture.GetHistoryAsync(token, workspaceRef, firstStuckRun.RefId, top: 100);
                string histJson = JsonSerializer.Serialize(history, JsonOptions);
                allSucceeded.Should().BeTrue($"expected at least {totalRunsExpected} runs, all succeeded. Actual counts: {counts}. Sample stuck run history: {histJson}");
            }
        }
        allSucceeded.Should().BeTrue($"expected at least {totalRunsExpected} runs, all succeeded. Actual counts: {counts}");

        // 7. Verify every one of the 1,000 (and the warm-up's) exists and none failed
        finalRuns.Should().HaveCountGreaterThanOrEqualTo(totalRunsExpected);
        finalRuns.Should().OnlyContain(r => r.Status == "Succeeded");
    }
}
