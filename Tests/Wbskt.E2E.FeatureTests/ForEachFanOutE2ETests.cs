using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests;

/// <summary>
/// ForEach fan-out — proves a single trigger fans out into N independent body branches,
/// each delivering its own device command. The per-branch action-idempotency key includes
/// BranchId, so the siblings are NOT deduped against each other.
///
/// Topology:
///   DeviceTrigger ─► Variable(Set Local "items"=[a,b,c]) ─► ForEach("items")
///                                                              ├─body─► action:command (OpenVent)
///                                                              └─done─► End
///
/// The collection is injected via a Variable node rather than read from telemetry because the
/// inbound payload is delivered as an opaque JSON string (trigger.payload), not a parsed array.
/// ForEach forks one "body" branch per item and the parent continues on the "done" port to an
/// explicit End (the parent branch must have a resolvable continuation — an unconnected fork
/// continue-port would throw). We assert the device receives exactly N commands, the run reaches
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

        // ── 1. Admin auth + policy + device registration ─────────────────────
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

        // ── 3. Connect the device and count inbound commands ─────────────────
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: deviceName,
            PolicyPin: null);

        var commandLock = new object();
        var commands = new List<string>();

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnCommandReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        // ── 4. Send telemetry → run starts and fans out ─────────────────────
        await wbsktClient.SendTelemetryAsync("telemetry", new { sensor = "foreach-test", value = 1 });

        // Capture the single run this telemetry started (workflow is unique to this test).
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty, "telemetry should have started exactly one run");

        // ── 5. The run finalizes as Succeeded (parent + all body branches done) ─
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(40));
        summary.Should().NotBeNull("the run must finalize");
        summary!.Status.Should().Be("Succeeded", "every fanned-out branch completes successfully");

        // ── 6. The device must receive exactly one command per item ──────────
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
            allArrived.Should().BeTrue($"ForEach should fan out into {Items.Length} command deliveries; saw {commands.Count}");
            commands.Should().HaveCount(Items.Length, "fan-out siblings must not be deduped against each other");
            commands.Should().OnlyContain(c => c == "OpenVent");
        }

        // ── 7. Each fanned-out branch carried a distinct item (binding correctness) ─
        var detail = await fixture.GetRunDetailAsync(token, workspaceRef, runRefId);
        var boundItems = detail.Branches
            .Select(b => TryGetLocalItem(b.LocalJson))
            .Where(i => i is not null)
            .Select(i => i!)
            .OrderBy(i => i, StringComparer.Ordinal)
            .ToList();
        boundItems.Should().BeEquivalentTo(Items, "each body branch must bind a distinct ForEach item");
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

    private static WorkflowDefinition BuildForEachDefinition(Guid workflowRefId, string deviceRef)
    {
        var triggerNodeId = Guid.NewGuid();
        var variableNodeId = Guid.NewGuid();
        var forEachNodeId = Guid.NewGuid();
        var actionNodeId = Guid.NewGuid();
        var endNodeId = Guid.NewGuid();

        var triggerNode = new DeviceTriggerNode(
            NodeId: triggerNodeId,
            Name: "Device Trigger",
            Ports: [new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new DeviceTriggerConfig(
                DeviceRef: deviceRef,
                Event: "telemetry",
                CorrelationKey: null,
                ConcurrencyPolicy: WorkflowConcurrencyPolicy.AllowParallel));

        var itemsValue = JsonSerializer.SerializeToElement(Items);
        var variableNode = new VariableNode(
            NodeId: variableNodeId,
            Name: "Set Items",
            Ports:
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("default", PortDirection.Output, "Out")
            ],
            Config: new VariableConfig(VariableScope.Local, VariableOperation.Set, "items", itemsValue));

        var forEachNode = new ForEachNode(
            NodeId: forEachNodeId,
            Name: "For Each Item",
            Ports:
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("body", PortDirection.Output, "Body"),
                new PortDefinition("done", PortDirection.Output, "Done")
            ],
            Config: new ForEachConfig("items"));

        var actionNode = new SendCommandActionNode(
            NodeId: actionNodeId,
            Name: "OpenVent",
            Ports:
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("default", PortDirection.Output, "Out")
            ],
            Config: new SendCommandConfig(DeviceRef: deviceRef, Command: "OpenVent", Payload: null));

        var endNode = new EndNode(
            NodeId: endNodeId,
            Name: "End",
            Ports: [new PortDefinition("in", PortDirection.Input, "In")]);

        var edges = new[]
        {
            new Edge(From: (triggerNodeId, "default"), To: (variableNodeId, "in")),
            new Edge(From: (variableNodeId, "default"), To: (forEachNodeId, "in")),
            new Edge(From: (forEachNodeId, "body"), To: (actionNodeId, "in")),
            new Edge(From: (forEachNodeId, "done"), To: (endNodeId, "in"))
        };

        return new WorkflowDefinition(
            WorkflowRefId: workflowRefId,
            Version: 1,
            WorkspaceId: 1,
            Name: $"E2E-ForEach-{workflowRefId:N}",
            Description: null,
            IsEnabled: true,
            Nodes: [triggerNode, variableNode, forEachNode, actionNode, endNode],
            Edges: edges,
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1);
    }
}
