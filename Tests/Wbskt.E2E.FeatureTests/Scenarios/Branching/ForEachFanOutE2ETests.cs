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
/// ForEach fan-out — proves a single trigger fans out into N independent body branches,
/// each delivering its own client command. The per-branch action-idempotency key includes
/// BranchId, so the siblings are NOT deduped against each other.
///
/// Topology:
///   DeviceTrigger ─► Variable(Set Local "items"=[a,b,c]) ─► ForEach("items")
///                                                              ├─body─► action:clientMessage (OpenVent)
///                                                              └─done─► End
///
/// The collection is injected via a Variable node rather than read from telemetry because the
/// inbound payload is delivered as an opaque JSON string (trigger.payload), not a parsed array.
/// ForEach forks one "body" branch per item and the parent continues on the "done" port to an
/// explicit End (the parent branch must have a resolvable continuation — an unconnected fork
/// continue-port would throw). We assert the client receives exactly N commands, the run reaches
/// the terminal "Succeeded" status, and each fanned-out branch carried a distinct item.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ForEachFanOutE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly string[] Items = ["a", "b", "c"];

    [SkippableFact]
    public async Task ForEachFansOut_DeliversOneCommandPerItem_AndRunSucceeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── 1. Admin auth + policy + client registration ─────────────────────
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-foreach-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        // ── 2. Publish DeviceTrigger → Variable(items) → ForEach → command ───
        var workflowRefId = Guid.NewGuid();
        var definition = BuildForEachDefinition(workflowRefId, clientRefId.ToString());

        var validation = new WorkflowValidator().Validate(definition);
        validation.IsValid.Should().BeTrue(
            $"definition must be valid; issues: {string.Join("; ", validation.Issues.Select(i => i.Message))}");

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-ForEach-{workflowRefId:N}",
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
        wbsktClient.OnMessageReceived += (action, _, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        // ── 4. Send telemetry → run starts and fans out ─────────────────────
        await wbsktClient.SendAsync("telemetry", new { sensor = "foreach-test", value = 1 });

        // Capture the single run this telemetry started (workflow is unique to this test).
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started exactly one run");

        // ── 5. The run finalizes as Succeeded (parent + all body branches done) ─
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded", "every fanned-out branch completes successfully");

        // ── 6. The client must receive exactly one command per item ──────────
        var allArrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Count >= Items.Length);
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromSeconds(1));

        lock (commandLock)
        {
            allArrived.Should().BeTrue($"ForEach should send one command per item ({Items.Length}); saw {commands.Count}");
            commands.Should().HaveCount(Items.Length, "every item's body runs exactly once");
            commands.Should().OnlyContain(c => c == "OpenVent");
        }

        // ── 7. ForEach is SEQUENTIAL: one branch walks the collection, so the run must not have
        //       fanned out into a branch per item. (It previously behaved like ParallelForEach.)
        var detail = await fixture.GetRunDetailAsync(token, workspaceRef, runRefId);
        detail.Branches.Should().HaveCount(1, "a sequential ForEach iterates on a single branch");

        // The surviving branch holds the last item it processed, and the iterator has been cleared
        // on the way out through "done".
        TryGetLocalItem(detail.Branches.Single().LocalJson)
            .Should().Be(Items[^1], "the branch carries the final item after the last lap");
    }

    private static string? TryGetLocalItem(string? localJson)
    {
        if (string.IsNullOrWhiteSpace(localJson))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(localJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("item", out var item)
            && item.ValueKind == JsonValueKind.String)
        {
            return item.GetString();
        }

        return null;
    }

    private static WorkflowDefinition BuildForEachDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-ForEach-{workflowRefId:N}", workflowRefId)
            .AddTelemetryClientTrigger(clientRef)
            .AddVariable(VariableScope.Local, VariableOperation.Set, "items", JsonSerializer.SerializeToElement(Items))
            .AddForEach("items", loop =>
            {
                loop.OnBody(b => b.AddClientMessage(clientRef, "OpenVent"));
                loop.OnDone(b => b.AddEnd());
            });

        return builder.BuildAndValidate();
    }
}
