using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>
/// #772: a step the designer has just dropped carries only its seeded keys. It must reach the validator
/// and be listed as unfinished, not fail the request as a serializer 400.
/// </summary>
public sealed class NewStepValidationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly WorkflowValidator Validator = new();

    [Theory]
    [InlineData("trigger:client", """{ "type": "*", "holdSeconds": 0, "concurrencyPolicy": "Queue" }""", "TRIGGER_CLIENT_REQUIRED")]
    [InlineData("trigger:client", """{ "clientRef": "sensor-A", "holdSeconds": 0 }""", "TRIGGER_MESSAGE_TYPE_REQUIRED")]
    [InlineData("trigger:clientPresence", """{ "state": "Offline", "forSeconds": 60, "concurrencyPolicy": "Queue" }""", "TRIGGER_CLIENT_REQUIRED")]
    [InlineData("trigger:webhook", """{ "concurrencyPolicy": "Queue" }""", "WEBHOOK_PATH_MISSING")]
    [InlineData("trigger:schedule", """{}""", "INVALID_CRON")]
    [InlineData("control:logic", """{}""", "LOGIC_CONDITION_MISSING")]
    [InlineData("control:fork", """{}""", "FORK_BRANCHES_MISSING")]
    public void A_new_step_is_listed_as_unfinished(string kind, string config, string code)
    {
        BaseNode node = Deserialize(kind, config);
        WorkflowDefinition definition = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(definition with { Nodes = [.. definition.Nodes, node] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == code && i.NodeId == node.NodeId);
    }

    [Fact]
    public void A_client_trigger_with_a_hold_needs_a_client_id()
    {
        // The hold key is built from the parsed GUID; a plain message trigger matches the reference as sent.
        BaseNode node = Deserialize("trigger:client", """{ "clientRef": "sensor-A", "type": "telemetry", "holdSeconds": 30 }""");
        WorkflowDefinition definition = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(definition with { Nodes = [.. definition.Nodes, node] });

        Assert.Contains(result.Issues, i => i.Code == "INVALID_CLIENT_REF" && i.NodeId == node.NodeId);
    }

    private static BaseNode Deserialize(string kind, string config) =>
        JsonSerializer.Deserialize<BaseNode>(
            $$"""{ "nodeId": "{{Guid.NewGuid()}}", "kind": "{{kind}}", "name": "New step", "ports": [], "config": {{config}} }""",
            Options)!;
}
