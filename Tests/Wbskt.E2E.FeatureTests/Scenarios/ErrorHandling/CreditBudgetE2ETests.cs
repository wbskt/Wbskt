using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

[Collection(E2ECollection.Name)]
public sealed class CreditBudgetE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task InfiniteLoop_ExhaustsCreditBudget_AndTransitionsToFailed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceCreditBudget");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceCreditBudget",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-CreditBudget-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _);

        builder.AddLogicGate("true", logic => 
        {
            // Loop the "true" port right back to the LogicGate!
            builder.Connect(logic.GateId, PortNames.True, logic.GateId, PortNames.In);
            
            // We'll also wire "false" to a success node just in case (though it won't be hit)
            logic.OnFalse(b => b.AddClientMessage(deviceRefStr, "Done", "Done", null));
        });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        await wbsktClient.StartAsync();

        // Start the infinite loop
        await wbsktClient.SendTelemetryAsync("telemetry", new { loop = true });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        try
        {
            // Wait for the run to transition to terminal.
            var summary = await fixture.WaitForRunStatusAsync(token, workspaceRef, publishedRef, "Failed", TimeSpan.FromSeconds(60));
            
            if (summary == null || summary.Status != "Failed")
            {
                var debugHistory = await fixture.GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
                throw new Exception($"Run is {summary?.Status ?? "still running"} instead of Failed. Executed {debugHistory.Count} events. Last event: {debugHistory.LastOrDefault()?.EventKind}");
            }
            
            // Assert the history event was recorded
            var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
            history.Should().Contain(e => e.EventKind == "NodeFailed" && e.PayloadJson!.Contains("OUT_OF_CREDITS"));
        }
        finally
        {
            try
            {
                await fixture.CancelRunAsync(token, workspaceRef, runRefId);
            }
            catch
            {
                // Ignore cancellation errors if it already finished
            }
        }
    }
}
