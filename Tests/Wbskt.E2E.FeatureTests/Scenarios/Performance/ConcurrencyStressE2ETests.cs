using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.E2E.FeatureTests.Scenarios.Performance;

[Collection(E2ECollection.Name)]
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
        Skip.IfNot(_fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // 1. Setup Admin & workspace
        var (token, workspaceRef) = await _fixture.LoginAsAdminAsync();

        // 2. Define and Publish the Stress Workflow
        // Fork into 5 branches -> Delay 1s -> Join
        Guid workflowRefId = Guid.NewGuid();
        var builder = new WorkflowBuilder($"E2E-Stress-{workflowRefId:N}", workflowRefId)
            .AddWebhookTrigger("stress", "POST", WorkflowConcurrencyPolicy.AllowParallel, out Guid triggerId);

        builder.AddFork(["b1", "b2", "b3", "b4", "b5"], out Guid forkId);

        // 5 branches
        Guid[] delayNodes = new Guid[5];
        for (int i = 0; i < 5; i++)
        {
            builder.SetHead(forkId, $"b{i + 1}")
                   .AddDelay(TimeSpan.FromSeconds(1), out Guid delayId);
            delayNodes[i] = delayId;
        }

        builder.ClearHead();
        builder.AddJoin(JoinMode.All, out Guid joinId);

        // Link all delays to the Join
        foreach (var delayId in delayNodes)
        {
            builder.Connect(delayId, "default", joinId, "in");
        }

        // 3. Publish
        var definitionElement = JsonSerializer.SerializeToElement(builder.Build(), JsonOptions);
        await _fixture.PublishWorkflowAsync(token, workspaceRef, workflowRefId, "StressTest", definitionElement);
        await Task.Delay(1000); // Give TriggerDispatcher cache time to invalidate

        // 4. Fire 1,000 webhooks concurrently (100 tasks * 10 webhooks)
        int numTasks = 100;
        int webhooksPerTask = 10;
        int totalRunsExpected = numTasks * webhooksPerTask;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(E2EConfig.WorkflowBaseUrl) };

        var tasks = new List<Task>();

        for (int i = 0; i < numTasks; i++)
        {
            int taskIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < webhooksPerTask; j++)
                {
                    var payload = new { task = taskIndex, iter = j };
                    using var resp = await client.PostAsJsonAsync("/api/inbound/webhook/stress", payload);
                    if (!resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync();
                        throw new Exception($"Webhook returned {resp.StatusCode}: {body}");
                    }
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
                int succeeded = runs.Count(r => r.Status == "Succeeded");
                return succeeded == totalRunsExpected;
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
                allSucceeded.Should().BeTrue($"expected exactly {totalRunsExpected} runs to succeed. Actual counts: {counts}. Sample stuck run history: {histJson}");
            }
        }
        allSucceeded.Should().BeTrue($"expected exactly {totalRunsExpected} runs to succeed. Actual counts: {counts}");

        // 7. Verify exactly 1,000 runs exist and 0 failures
        finalRuns.Should().HaveCount(totalRunsExpected);
        finalRuns.Should().OnlyContain(r => r.Status == "Succeeded");
    }
}
