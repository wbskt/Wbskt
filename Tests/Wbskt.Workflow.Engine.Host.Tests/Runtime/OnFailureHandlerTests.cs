using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class OnFailureHandlerTests
{
    private static readonly BranchContext Context = new(
        42,
        1001,
        9,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        1,
        Guid.Parse("11111111-1111-1111-1111-111111111111").ToString(),
        1,
        new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>(),
        "corr-42",
        new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Apply_with_fail_branch_returns_original_fail()
    {
        var handler = new OnFailureHandler();
        var fail = new NodeExecutionResult.Fail("E_FAIL", "boom", false, null);
        var node = CreateNode(new OnFailureConfig(ErrorOutcome.FailBranch));

        NodeExecutionResult result = handler.Apply(fail, node, Context);

        Assert.Same(fail, result);
    }

    [Fact]
    public void Apply_with_continue_as_succeeded_returns_continue_with_last_error_patch()
    {
        var handler = new OnFailureHandler();
        var fail = new NodeExecutionResult.Fail("E_FAIL", "boom", false, null);
        var node = CreateNode(new OnFailureConfig(ErrorOutcome.ContinueAsSucceeded));

        NodeExecutionResult.Continue result = Assert.IsType<NodeExecutionResult.Continue>(handler.Apply(fail, node, Context));

        Assert.Equal("default", result.OutboundPort);
        JsonElement lastError = result.LocalStatePatch["lastError"];
        Assert.Equal("E_FAIL", lastError.GetProperty("errorCode").GetString());
        Assert.Equal("boom", lastError.GetProperty("message").GetString());
    }

    [Fact]
    public void Apply_with_jump_to_node_returns_jump_continue_with_last_error_patch()
    {
        var handler = new OnFailureHandler();
        var fail = new NodeExecutionResult.Fail("E_FAIL", "boom", false, null);
        Guid targetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var node = CreateNode(new OnFailureConfig(ErrorOutcome.JumpToNode, TargetNodeId: targetNodeId));

        NodeExecutionResult.Continue result = Assert.IsType<NodeExecutionResult.Continue>(handler.Apply(fail, node, Context));

        Assert.Equal(targetNodeId.ToString(), result.OutboundPort);
        Assert.True(result.LocalStatePatch.ContainsKey("lastError"));
    }

    private static SendCommandActionNode CreateNode(OnFailureConfig onFailure)
    {
        return new SendCommandActionNode(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "command",
            [new PortDefinition("default", PortDirection.Output, "Default")],
            new SendCommandConfig("device-1", "DoThing"),
            null,
            onFailure);
    }
}
