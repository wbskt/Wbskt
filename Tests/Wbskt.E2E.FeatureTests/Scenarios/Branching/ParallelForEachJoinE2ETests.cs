using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Branching;

/// <summary>
/// ParallelForEach + Join — proves a fan-out/fan-in barrier: N parallel body branches converge
/// on a Join node that elects exactly ONE winner to continue (the others terminate quietly), so
/// the downstream action fires once regardless of fan-out width.
///
/// Topology:
///   DeviceTrigger ─► Variable(Set Local "items"=[a,b,c]) ─► ParallelForEach("items")
///                                                              └─body─► Join(All) ─► action:clientMessage (OpenVent)
///
/// ParallelForEach forks one "body" branch per item (the parent terminates — continue is null),
/// each branch reaches Join, and the Join aggregator's compare-and-swap lets a single winner take
/// the "default" port to SendCommand while the losers terminate as Completed. We assert exactly
/// ONE command is delivered and the run reaches the terminal "Succeeded" status.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ParallelForEachJoinE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly string[] Items = ["a", "b", "c"];

    [SkippableFact]
    public async Task ParallelForEachJoinsToSingleContinuation_DeliversExactlyOneCommand()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + policy + client registration ─────────────────────
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-pfe-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Publish trigger → Variable → ParallelForEach → Join → command ─
        var workflowRefId = Guid.NewGuid();
        var definition = BuildParallelForEachDefinition(workflowRefId, clientRefId.ToString());

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-PFE-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // ── 3. Connect the client and count inbound commands ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

        var commandLock = new object();
        var commands = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        // ── 4. Send telemetry → run starts, fans out and joins ───────────────
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "pfe-test", value = 1 });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started exactly one run");

        // ── 5. The run finalizes as Succeeded (winner + losers + parent done) ─
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded", "the join winner completes and the losers terminate cleanly");

        // ── 6. Exactly one command must arrive (single join continuation) ────
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Count >= 1);
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));
        arrived.Should().BeTrue("the join winner must send the command");

        // Give any erroneous extra continuations a window to surface, then assert exactly one.
        await Task.Delay(TimeSpan.FromSeconds(3));
        lock (commandLock)
        {
            commands.Should().ContainSingle("the Join must elect exactly one winner to continue past the barrier")
                .Which.Should().Be("OpenVent");
        }
    }

    private static WorkflowDefinition BuildParallelForEachDefinition(Guid workflowRefId, string clientRef)
    {
        return new WorkflowBuilder($"E2E-PFE-{workflowRefId:N}", workflowRefId)
            .AddTelemetryClientTrigger(clientRef)
            .AddVariable(VariableScope.Local, VariableOperation.Set, "items", JsonSerializer.SerializeToElement(Items))
            .AddParallelForEach("items", loop => 
            {
                loop.OnBody(b => 
                {
                    b.AddJoin(JoinMode.All, out _)
                     .AddClientMessage(clientRef, "OpenVent");
                });
            })
            .BuildAndValidate();
    }
}
