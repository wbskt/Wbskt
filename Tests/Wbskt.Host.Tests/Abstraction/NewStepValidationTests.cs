using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Infrastructure.Json;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>
/// #772: a step the designer has just dropped carries only its seeded keys. The request body that holds
/// it must bind, and the validator must list the step as unfinished, rather than the serializer answering
/// with a 400.
/// </summary>
public sealed class NewStepValidationTests
{
    private static readonly JsonSerializerOptions Options = WbsktJsonExtensions.Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static readonly WorkflowValidator Validator = new();

    [Theory]
    [InlineData("trigger:client", """{ "type": "*", "holdSeconds": 0, "concurrencyPolicy": "Queue" }""", "TRIGGER_CLIENT_REQUIRED")]
    [InlineData("trigger:client", """{ "clientRef": "  ", "type": "telemetry" }""", "TRIGGER_CLIENT_REQUIRED")]
    [InlineData("trigger:client", """{ "clientRef": "sensor-A", "holdSeconds": 0 }""", "TRIGGER_MESSAGE_TYPE_REQUIRED")]
    [InlineData("trigger:clientPresence", """{ "state": "Offline", "forSeconds": 60, "concurrencyPolicy": "Queue" }""", "TRIGGER_CLIENT_REQUIRED")]
    [InlineData("trigger:clientPresence", """{ "clientRef": "sensor-A" }""", "INVALID_CLIENT_REF")]
    [InlineData("trigger:webhook", """{ "concurrencyPolicy": "Queue" }""", "WEBHOOK_PATH_MISSING")]
    [InlineData("trigger:webhook", """{ "path": " " }""", "WEBHOOK_PATH_MISSING")]
    [InlineData("trigger:schedule", """{}""", "INVALID_CRON")]
    [InlineData("control:logic", """{}""", "LOGIC_CONDITION_MISSING")]
    [InlineData("control:fork", """{}""", "FORK_BRANCHES_MISSING")]
    public void A_new_step_is_listed_as_unfinished(string kind, string config, string code)
    {
        (WorkflowPublishRequest request, Guid nodeId) = PublishWith(kind, config);

        var result = Validator.Validate(request.Definition);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == code && i.NodeId == nodeId);
    }

    [Fact]
    public void A_client_trigger_with_a_hold_needs_a_client_id()
    {
        // The hold key is built from the parsed GUID, as a presence key is.
        (WorkflowPublishRequest request, Guid nodeId) = PublishWith("trigger:client", """{ "clientRef": "sensor-A", "type": "telemetry", "holdSeconds": 30 }""");

        var result = Validator.Validate(request.Definition);

        Assert.Contains(result.Issues, i => i.Code == "INVALID_CLIENT_REF" && i.NodeId == nodeId);
    }

    [Fact]
    public void A_plain_client_trigger_takes_the_reference_as_the_socket_host_sends_it()
    {
        (WorkflowPublishRequest request, Guid nodeId) = PublishWith("trigger:client", """{ "clientRef": "sensor-A", "type": "telemetry" }""");

        var result = Validator.Validate(request.Definition);

        Assert.DoesNotContain(result.Issues, i => i.NodeId == nodeId && i.Code is "INVALID_CLIENT_REF" or "TRIGGER_CLIENT_REQUIRED");
    }

    /// <summary>A publish request, as the designer sends it, for a valid workflow with one new step added.</summary>
    private static (WorkflowPublishRequest Request, Guid NodeId) PublishWith(string kind, string config)
    {
        Guid nodeId = Guid.NewGuid();
        JsonNode definition = JsonSerializer.SerializeToNode(ValidWorkflowBuilder.Build(), Options)!;
        definition["nodes"]!.AsArray().Add(JsonNode.Parse($$"""{ "nodeId": "{{nodeId}}", "kind": "{{kind}}", "name": "New step", "ports": [], "config": {{config}} }"""));
        var body = new JsonObject
        {
            ["refId"] = Guid.NewGuid().ToString(),
            ["name"] = "wf",
            ["description"] = null,
            ["definition"] = definition
        };

        return (JsonSerializer.Deserialize<WorkflowPublishRequest>(body.ToJsonString(), Options)!, nodeId);
    }
}
