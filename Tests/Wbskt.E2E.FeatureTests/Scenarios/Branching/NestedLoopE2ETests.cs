using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Branching;

[Collection(E2ECollection.Name)]
public sealed class NestedLoopE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task DeepNestedBranching_CompletesRunWithoutHanging()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-nested-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildNestedLoopDefinition(workflowRefId, clientRefId.ToString());

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Nested-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

        var commandLock = new object();
        int commandCount = 0;

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _) =>
        {
            if (action == "ReachEnd")
            {
                lock (commandLock)
                {
                    commandCount++;
                }
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "nested-test" });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(60));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded", "the run completes after spawning and collapsing dozens of branches");

        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commandCount == 31); // 3 items * 5 items * 2 paths = 30 branches + 1 done branch = 31
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("all 31 inner branches must send the command");
    }

    private static WorkflowDefinition BuildNestedLoopDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-Nested-{workflowRefId:N}", workflowRefId)
            .AddTelemetryClientTrigger(clientRef)
            .AddVariable(VariableScope.Local, VariableOperation.Set, "items", JsonSerializer.SerializeToElement(new[] { "1", "2", "3" }))
            .AddForEach("items", forEach1 =>
            {
                forEach1.OnBody(b1 =>
                {
                    b1.AddVariable(VariableScope.Local, VariableOperation.Set, "subItems", JsonSerializer.SerializeToElement(new[] { "a", "b", "c", "d", "e" }))
                      .AddParallelForEach("subItems", pfe =>
                      {
                          pfe.OnBody(b2 =>
                          {
                              b2.AddFork(["path1", "path2"], fork =>
                              {
                                  fork.Branch("path1", b3 => b3.AddClientMessage(clientRef, "ReachEnd"));
                                  fork.Branch("path2", b3 => b3.AddClientMessage(clientRef, "ReachEnd"));
                              });
                          });
                      });
                });
                
                forEach1.OnDone(b1 => b1.AddClientMessage(clientRef, "ReachEnd"));
            });

        return builder.BuildAndValidate();
    }
}
