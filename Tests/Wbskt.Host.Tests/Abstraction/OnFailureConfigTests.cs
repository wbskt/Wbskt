using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Host.Tests.Abstraction;

public class OnFailureConfigTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OnFailureConfig_deserialises_FailRun_outcome()
    {
        var json = """{ "outcome": "FailRun" }""";
        var config = JsonSerializer.Deserialize<OnFailureConfig>(json, Options);
        Assert.NotNull(config);
        Assert.Equal(ErrorOutcome.FailRun, config!.Outcome);
    }

    [Fact]
    public void A_saved_Compensate_outcome_still_deserialises()
    {
        // Compensation is not available, and the validator rejects it at publish, but a definition saved
        // before that may carry the outcome (and its old "compensate" block). It must still load, so the
        // run fails plainly rather than the whole definition becoming unreadable.
        var json = """{ "outcome": "Compensate", "compensate": { "nodeId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "kind": "action:clientMessage" } }""";

        var config = JsonSerializer.Deserialize<OnFailureConfig>(json, Options);

        Assert.Equal(ErrorOutcome.Compensate, config!.Outcome);
    }

    [Fact]
    public void OnFailureConfig_roundtrips_with_jump_target()
    {
        var targetNodeId = new Guid("11111111-2222-3333-4444-555555555555");
        var config = new OnFailureConfig { Outcome = ErrorOutcome.JumpToNode, TargetNodeId = targetNodeId };

        var json = JsonSerializer.Serialize(config, Options);
        var back = JsonSerializer.Deserialize<OnFailureConfig>(json, Options);

        Assert.Equal(ErrorOutcome.JumpToNode, back!.Outcome);
        Assert.Equal(targetNodeId, back.TargetNodeId);
    }
}
