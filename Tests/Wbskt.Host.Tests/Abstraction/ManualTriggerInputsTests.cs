using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.NodeExecutors.Triggers;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>
/// The inputs a manual trigger declares for Run now (#200): checked at publish, and a missing one
/// takes its default when the run starts.
/// </summary>
public sealed class ManualTriggerInputsTests
{
    private static readonly Guid ManualId = new("bbbbbbbb-0000-0000-0000-000000000001");

    [Fact]
    public void Inputs_with_good_names_publish()
    {
        var result = Validate(Input("client"), Input("reason", "\"testing\""), Input("_count2"));

        Assert.DoesNotContain(result.Issues, i => i.Code.StartsWith("MANUAL_INPUT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "MANUAL_INPUT_NAME_INVALID")]
    [InlineData("2fast", "MANUAL_INPUT_NAME_INVALID")]
    [InlineData("has space", "MANUAL_INPUT_NAME_INVALID")]
    [InlineData("parentRunRefId", "MANUAL_INPUT_NAME_RESERVED")]
    [InlineData("CorrelationKey", "MANUAL_INPUT_NAME_RESERVED")]
    [InlineData("depth", "MANUAL_INPUT_NAME_RESERVED")]
    public void A_bad_or_reserved_name_is_refused_on_that_input(string name, string code)
    {
        var issue = Assert.Single(Validate(Input("client"), Input(name)).Issues, i => i.Code == code);

        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(ManualId, issue.NodeId);
        Assert.Equal("config.inputs[1].name", issue.Field);
    }

    [Fact]
    public void A_repeated_name_is_refused_on_the_second()
    {
        var issue = Assert.Single(Validate(Input("client"), Input("Client")).Issues, i => i.Code == "MANUAL_INPUT_NAME_DUPLICATE");

        Assert.Equal("config.inputs[1].name", issue.Field);
    }

    [Fact]
    public async Task A_missing_input_takes_its_default_and_a_sent_one_is_kept()
    {
        var node = Manual(Input("client", "\"porch\""), Input("reason", "\"by hand\""), Input("count", "3"), Input("note"));
        var payload = new Dictionary<string, JsonElement>
        {
            ["workflowDefinitionRefId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            ["body"] = JsonSerializer.SerializeToElement(new { reason = "maintenance" })
        };

        var trigger = await RunAsync(node, payload);

        var body = trigger.GetProperty("body");
        Assert.Equal("porch", body.GetProperty("client").GetString());
        Assert.Equal("maintenance", body.GetProperty("reason").GetString());
        Assert.Equal(3, body.GetProperty("count").GetInt32());
        Assert.False(body.TryGetProperty("note", out _), "an input with no default stays absent");
        Assert.True(trigger.TryGetProperty("workflowDefinitionRefId", out _));
    }

    [Fact]
    public async Task No_body_at_all_is_just_the_defaults()
    {
        var trigger = await RunAsync(Manual(Input("client", "\"porch\"")), new Dictionary<string, JsonElement>());

        Assert.Equal("porch", trigger.GetProperty("body").GetProperty("client").GetString());
    }

    private static async Task<JsonElement> RunAsync(ManualTriggerNode node, IReadOnlyDictionary<string, JsonElement> payload)
    {
        var context = new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), payload, "corr-1", DateTime.UtcNow, 9),
            Node = node,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "test-key"
        };

        var result = Assert.IsType<NodeExecutionResult.Continue>(await new ManualTriggerExecutor(TimeProvider.System).ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("default", result.OutboundPort);
        return result.LocalStatePatch["trigger"];
    }

    private static ValidationResult Validate(params ManualTriggerInput[] inputs)
    {
        var def = ValidWorkflowBuilder.Build();
        return new WorkflowValidator().Validate(def with { Nodes = [.. def.Nodes, Manual(inputs)] });
    }

    private static ManualTriggerNode Manual(params ManualTriggerInput[] inputs) => new()
    {
        NodeId = ManualId,
        Name = "Run by hand",
        Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
        Config = new ManualTriggerConfig { Inputs = inputs }
    };

    private static ManualTriggerInput Input(string name, string? defaultJson = null) => new()
    {
        Name = name,
        Default = defaultJson is null ? null : JsonDocument.Parse(defaultJson).RootElement.Clone()
    };
}
