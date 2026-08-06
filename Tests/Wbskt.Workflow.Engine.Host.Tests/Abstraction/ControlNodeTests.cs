using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class ControlNodeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LogicGateNode_deserialises_a_structured_condition()
    {
        // The form that actually supports comparisons: reading.temperature > 35.
        var json = """
            {
              "nodeId": "22222222-2222-2222-2222-222222222222",
              "kind": "control:logic",
              "name": "Gate",
              "ports": [],
              "config": {
                "condition": {
                  "kind": "binary",
                  "left": { "kind": "branchStateRef", "path": "reading.temperature" },
                  "operator": "GreaterThan",
                  "right": { "kind": "literal", "value": 35 }
                }
              }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var logic = Assert.IsType<LogicGateNode>(node);

        var binary = Assert.IsType<BinaryExpression>(logic.Config!.Condition);
        Assert.Equal(BinaryOperator.GreaterThan, binary.Operator);
        Assert.Equal("reading.temperature", Assert.IsType<BranchStateRefExpression>(binary.Left).Path);
    }

    [Fact]
    public void LogicGateNode_still_accepts_the_legacy_string_condition()
    {
        // Definitions published before the condition became structured must keep deserialising, and
        // must keep their original meaning: a non-boolean string was a branch-state path.
        var json = """
            { "nodeId": "22222222-2222-2222-2222-222222222222", "kind": "control:logic", "name": "Gate", "ports": [], "config": { "condition": "decision" } }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var logic = Assert.IsType<LogicGateNode>(node);

        Assert.Equal("decision", Assert.IsType<BranchStateRefExpression>(logic.Config!.Condition).Path);
    }

    [Fact]
    public void LogicGateNode_legacy_boolean_string_condition_becomes_a_literal()
    {
        var json = """
            { "nodeId": "22222222-2222-2222-2222-222222222222", "kind": "control:logic", "name": "Gate", "ports": [], "config": { "condition": "true" } }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var logic = Assert.IsType<LogicGateNode>(node);

        Assert.Equal(true, Assert.IsType<LiteralExpression>(logic.Config!.Condition).Value);
    }

    [Fact]
    public void LogicGateNode_writes_a_legacy_condition_back_in_structured_form()
    {
        // Round-tripping upgrades a legacy definition, so republishing normalises it.
        var json = """
            { "nodeId": "22222222-2222-2222-2222-222222222222", "kind": "control:logic", "name": "Gate", "ports": [], "config": { "condition": "decision" } }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);

        string roundTripped = JsonSerializer.Serialize(node, Options);

        Assert.Contains("\"kind\":\"branchStateRef\"", roundTripped.Replace(" ", string.Empty));
        var reparsed = Assert.IsType<LogicGateNode>(JsonSerializer.Deserialize<BaseNode>(roundTripped, Options));
        Assert.Equal("decision", Assert.IsType<BranchStateRefExpression>(reparsed.Config!.Condition).Path);
    }

    [Fact]
    public void DelayNode_deserialises_duration()
    {
        var json = """{ "nodeId": "44444444-4444-4444-4444-444444444444", "kind": "control:delay", "name": "Delay", "ports": [], "config": { "duration": "00:10:00" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var delay = Assert.IsType<DelayNode>(node);
        Assert.Equal(TimeSpan.FromMinutes(10), delay.Config!.Duration);
    }

    [Fact]
    public void ForEachNode_deserialises_collection()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000021", "kind": "control:foreach", "name": "ForEach", "ports": [], "config": { "collection": "$trigger.items" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var fe = Assert.IsType<ForEachNode>(node);
        Assert.Equal("$trigger.items", fe.Config!.Collection);
    }

    [Fact]
    public void ParallelForEachNode_deserialises_collection()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000022", "kind": "control:parallelForEach", "name": "ParallelForEach", "ports": [], "config": { "collection": "$trigger.devices" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var pfe = Assert.IsType<ParallelForEachNode>(node);
        Assert.Equal("$trigger.devices", pfe.Config!.Collection);
    }

    [Fact]
    public void JoinNode_deserialises_all_mode()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000023", "kind": "control:join", "name": "Join", "ports": [], "config": { "mode": "All" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var join = Assert.IsType<JoinNode>(node);
        Assert.Equal(JoinMode.All, join.Config!.Mode);
    }

    [Fact]
    public void VariableNode_deserialises_increment_op()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000024", "kind": "control:variable", "name": "Var", "ports": [], "config": { "scope": "Shared", "op": "Increment", "var": "smsToday" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var v = Assert.IsType<VariableNode>(node);
        Assert.Equal(VariableScope.Shared, v.Config!.Scope);
        Assert.Equal(VariableOperation.Increment, v.Config.Op);
        Assert.Equal("smsToday", v.Config.Var);
    }

    [Fact]
    public void SubWorkflowNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000025", "kind": "control:subWorkflow", "name": "SubWF", "ports": [], "config": { "workflowRefId": "00000000-0000-0000-0000-000000000001" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var sw = Assert.IsType<SubWorkflowNode>(node);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), sw.Config!.WorkflowRefId);
    }

    [Fact]
    public void WaitForHttpNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000026", "kind": "control:waitForHttp", "name": "WaitHttp", "ports": [], "config": { "ttl": "01:00:00" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var wh = Assert.IsType<WaitForHttpNode>(node);
        Assert.Equal(TimeSpan.FromHours(1), wh.Config!.Ttl);
    }

    [Fact]
    public void AwaitSignalNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000027", "kind": "control:awaitSignal", "name": "AwaitSignal", "ports": [], "config": { "signalName": "operator-ack", "correlation": "$run.runId" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var as_ = Assert.IsType<AwaitSignalNode>(node);
        Assert.Equal("operator-ack", as_.Config!.SignalName);
        Assert.Equal("$run.runId", as_.Config.Correlation);
    }

    [Fact]
    public void FailRunNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000028", "kind": "control:failRun", "name": "FailRun", "ports": [], "config": { "reason": "max retries" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var fr = Assert.IsType<FailRunNode>(node);
        Assert.Equal("max retries", fr.Config!.Reason);
    }
}
