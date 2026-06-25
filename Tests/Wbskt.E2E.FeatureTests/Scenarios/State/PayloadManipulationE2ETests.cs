using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.E2E.FeatureTests.Scenarios.State;

[Collection(E2ECollection.Name)]
public class PayloadManipulationE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [SkippableFact]
    public async Task StateManipulation_JsonPathAndVariables_IsIsolatedPerBranch()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();

        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DevicePayload");
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DevicePayload",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var extractBatchId = new JsonPathExpression(new BranchStateRefExpression("trigger.payload"), "$.batchId");
        
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Iterate over docs array
        var extractDocs = new JsonPathExpression(new BranchStateRefExpression("trigger.payload"), "$.docs");

        // Inside Parallel For Each
        // We set itemData = item.value
        var extractItemValue = new JsonPathExpression(new BranchStateRefExpression("item"), "$.value");
        Guid joinNodeId = Guid.Empty;

        var builder = new WorkflowBuilder($"E2E-Payload-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), "process-docs", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddVariable(VariableScope.Local, VariableOperation.Set, "batchId", JsonSerializer.SerializeToElement<WorkflowExpression>(extractBatchId, options))
            .AddVariable(VariableScope.Local, VariableOperation.Set, "docsArray", JsonSerializer.SerializeToElement<WorkflowExpression>(extractDocs, options))
            .AddParallelForEach("docsArray", loop => 
            {
                loop.OnBody(b => 
                {
                    b.AddVariable(VariableScope.Local, VariableOperation.Set, "itemData", JsonSerializer.SerializeToElement<WorkflowExpression>(extractItemValue, options))
                     .AddDelay(TimeSpan.FromSeconds(1))
                     .AddJoin(JoinMode.All, out joinNodeId);
                });
            });

        var definition = builder.BuildAndValidate();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        var payload = new
        {
            batchId = "batch-99",
            docs = new[]
            {
                new { id = 101, value = "DocA" },
                new { id = 102, value = "DocB" },
                new { id = 103, value = "DocC" }
            }
        };

        await wbsktClient.SendTelemetryAsync("process-docs", payload);

        // Wait for run
        var summary = await fixture.WaitForRunStatusAsync(token, workspaceRef, publishedRef, "Succeeded", TimeSpan.FromSeconds(10));
        if (summary == null)
        {
            var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
            if (runs.Count > 0)
            {
                var stuckHistory = await fixture.GetHistoryAsync(token, workspaceRef, runs[0].RefId, top: 100);
                throw new Exception($"Run stuck in state {runs[0].Status}. History: {JsonSerializer.Serialize(stuckHistory)}");
            }
        }
        
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");

        var history = await fixture.GetHistoryAsync(token, workspaceRef, summary.RefId, top: 100);

        // Verify that the Join node was executed by each of the 3 branches
        var joinEvents = history.Where(e => e.EventKind == "NodeCompleted" && e.NodeId == joinNodeId).ToList();
        joinEvents.Should().HaveCount(3);
        
        // Only one of those executions should have resulted in a Continue ("port":"default")
        joinEvents.Count(e => e.PayloadJson != null && e.PayloadJson.Contains("\"port\":\"default\"")).Should().Be(1);
    }
}
