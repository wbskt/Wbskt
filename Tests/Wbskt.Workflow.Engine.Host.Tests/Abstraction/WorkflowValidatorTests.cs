using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class WorkflowValidatorTests
{
    private static readonly WorkflowValidator Validator = new();

    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_node()
    {
        var def = ValidWorkflowBuilder.Build() with
        {
            Edges = [new Edge((Guid.NewGuid(), "out"), (Guid.NewGuid(), "in"))]
        };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_NODE");
    }

    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_port()
    {
        var def = ValidWorkflowBuilder.Build();
        var existingNode = def.Nodes.First();
        var badEdge = new Edge((existingNode.NodeId, "nonexistent-port"), (existingNode.NodeId, "in"));
        var newDef = def with { Edges = [badEdge] };
        var result = Validator.Validate(newDef);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_PORT");
    }


    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_target_port()
    {
        var def = ValidWorkflowBuilder.Build();
        var fromNode = def.Nodes.First(n => n.NodeId == ValidWorkflowBuilder.Trigger1Id);
        var badEdge = new Edge((fromNode.NodeId, "out"), (ValidWorkflowBuilder.Logic1Id, "missing-in"));
        var newDef = def with { Edges = [badEdge] };
        var result = Validator.Validate(newDef);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_PORT" && i.NodeId == ValidWorkflowBuilder.Logic1Id);
    }

    [Fact]
    public void Validate_returns_error_when_node_ids_are_duplicated()
    {
        var def = ValidWorkflowBuilder.Build();
        var duplicate = new LogicGateNode { NodeId = ValidWorkflowBuilder.Logic1Id, Name = "Duplicate", Ports = [], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("true") } };
        var defWithDuplicate = def with { Nodes = [..def.Nodes, duplicate] };
        var result = Validator.Validate(defWithDuplicate);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "DUPLICATE_NODE_ID" && i.NodeId == ValidWorkflowBuilder.Logic1Id);
    }

    [Fact]
    public void Validate_returns_error_when_workflowRefId_is_empty()
    {
        var def = ValidWorkflowBuilder.Build() with { WorkflowRefId = Guid.Empty };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_WORKFLOW_REF_ID");
    }

    [Fact]
    public void Validate_returns_error_when_version_less_than_1()
    {
        var def = ValidWorkflowBuilder.Build() with { Version = 0 };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_VERSION");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-token")]
    public void Validate_returns_error_when_waitforhttp_token_missing_or_weak(string? token)
    {
        var def = ValidWorkflowBuilder.Build();
        var waitNode = new WaitForHttpNode
        {
            NodeId = new Guid("aaaaaaaa-0000-0000-0000-00000000009a"),
            Name = "wait",
            Ports =
            [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" },
                new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }
            ],
            Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(5), Token = token }
        };
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, waitNode] });
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "WAITFORHTTP_WEAK_TOKEN");
    }

    [Fact]
    public void Validate_allows_waitforhttp_with_strong_token()
    {
        var def = ValidWorkflowBuilder.Build();
        var waitNode = new WaitForHttpNode
        {
            NodeId = new Guid("aaaaaaaa-0000-0000-0000-00000000009b"),
            Name = "wait",
            Ports =
            [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" },
                new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }
            ],
            Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(5), Token = Guid.NewGuid().ToString("N") }
        };
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, waitNode] });
        Assert.DoesNotContain(result.Issues, i => i.Code == "WAITFORHTTP_WEAK_TOKEN");
    }

    [Fact]
    public void Validate_returns_warning_when_no_trigger_nodes()
    {
        var logicNode = new LogicGateNode { NodeId = new Guid("aaaaaaaa-0000-0000-0000-000000000004"), Name = "Logic", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("true") } };
        var defNoTriggers = ValidWorkflowBuilder.Build() with { Nodes = [logicNode], Edges = [] };
        var result = Validator.Validate(defNoTriggers);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "NO_TRIGGERS");
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_returns_warning_for_orphan_node()
    {
        var def = ValidWorkflowBuilder.Build();
        var orphanId = new Guid("aaaaaaaa-0000-0000-0000-000000000003");
        var orphan = new LogicGateNode { NodeId = orphanId, Name = "Orphan", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("true") } };
        var defWithOrphan = def with { Nodes = [..def.Nodes, orphan] };
        var result = Validator.Validate(defWithOrphan);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "ORPHAN_NODE" && i.NodeId == orphanId);
    }
}

public sealed class WorkflowValidatorRuleTests
{
    private static readonly WorkflowValidator Validator = new();

    // ------------------------------------------------- WF-18 duplicate edges

    [Fact]
    public void Validate_rejects_two_edges_leaving_the_same_port()
    {
        // BranchLoop resolves the next node with SingleOrDefault, so this throws mid-run rather than
        // merely being ambiguous.
        var def = ValidWorkflowBuilder.Build();
        var extra = new Edge((ValidWorkflowBuilder.Logic1Id, "true"), (ValidWorkflowBuilder.Trigger1Id, "in"));
        var another = new Edge((ValidWorkflowBuilder.Logic1Id, "true"), (ValidWorkflowBuilder.Logic1Id, "in"));

        var result = Validator.Validate(def with { Edges = [.. def.Edges, extra, another] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "DUPLICATE_PORT_EDGE");
    }

    [Fact]
    public void Validate_allows_one_edge_per_port()
    {
        var result = Validator.Validate(ValidWorkflowBuilder.Build());

        Assert.DoesNotContain(result.Issues, i => i.Code == "DUPLICATE_PORT_EDGE");
    }

    // --------------------------------------------------- WF-19.1 node kinds

    [Fact]
    public void Validate_rejects_a_kind_that_is_not_implemented()
    {
        // action:email publishes cleanly today and then fails the run at execution.
        var def = ValidWorkflowBuilder.Build();
        var email = new EmailNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "mail",
            Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new EmailConfig { To = "a@b", Subject = "s", Body = "b" }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, email] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "NODE_KIND_NOT_IMPLEMENTED");
    }

    [Fact]
    public void Validate_accepts_an_implemented_action_kind()
    {
        var def = ValidWorkflowBuilder.Build();
        var toast = new ToastNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "toast",
            Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new ToastConfig { Title = "t", Message = "m" }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, toast] });

        Assert.DoesNotContain(result.Issues, i => i.Code is "NODE_KIND_NOT_IMPLEMENTED" or "UNKNOWN_NODE_KIND");
    }

    // ------------------------------------------------- WF-19.2/.4 onFailure

    [Fact]
    public void Validate_rejects_ContinueOnError_without_an_error_port()
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with
        {
            OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.ContinueOnError }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT");
    }

    [Fact]
    public void Validate_accepts_ContinueOnError_when_an_error_port_is_declared()
    {
        // The optional "error" output must also not be reported as an unexpected extra port.
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: true) with
        {
            OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.ContinueOnError }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT");
        Assert.DoesNotContain(result.Issues, i => i.Code == "EXTRA_PORT");
    }

    [Fact]
    public void Validate_rejects_an_onFailure_jump_target_that_does_not_exist()
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with
        {
            OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.JumpToNode, TargetNodeId = Guid.NewGuid() }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "ONFAILURE_TARGET_NOT_FOUND");
    }

    // ---------------------------------------------- WF-09 sub-workflow inputs

    [Theory]
    [InlineData("parentRunRefId")]
    [InlineData("correlationKey")]
    public void Validate_rejects_a_SubWorkflow_input_that_shadows_an_engine_key(string reservedKey)
    {
        // parentRunRefId is how the child's completion hook finds its way back; an input of the same
        // name would overwrite it and strand the parent on its bookmark.
        var def = ValidWorkflowBuilder.Build();
        var subId = Guid.NewGuid();
        var sub = new SubWorkflowNode
        {
            NodeId = subId,
            Name = "sub",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
            ],
            Config = new SubWorkflowConfig
            {
                WorkflowRefId = Guid.NewGuid(),
                Input = new Dictionary<string, WorkflowExpression> { [reservedKey] = new LiteralExpression("x") }
            }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, sub] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "SUBWORKFLOW_INPUT_KEY_RESERVED" && i.NodeId == subId);
    }

    [Fact]
    public void Validate_accepts_a_SubWorkflow_with_ordinary_inputs()
    {
        var def = ValidWorkflowBuilder.Build();
        var subId = Guid.NewGuid();
        var sub = new SubWorkflowNode
        {
            NodeId = subId,
            Name = "sub",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
            ],
            Config = new SubWorkflowConfig
            {
                WorkflowRefId = Guid.NewGuid(),
                Input = new Dictionary<string, WorkflowExpression> { ["orderId"] = new BranchStateRefExpression("orderId") }
            }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, sub] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "SUBWORKFLOW_INPUT_KEY_RESERVED");
    }

    // ---------------------------------------------- WF-19.3 PFE <-> Join

    [Fact]
    public void Validate_rejects_a_ParallelForEach_with_no_Join_downstream()
    {
        var def = ValidWorkflowBuilder.Build();
        var pfeId = Guid.NewGuid();
        var tailId = Guid.NewGuid();
        var pfe = new ParallelForEachNode
        {
            NodeId = pfeId,
            Name = "pfe",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" },
                new PortDefinition { PortId = "empty", Direction = PortDirection.Output, Label = "Empty" }
            ],
            Config = new ParallelForEachConfig { Collection = "items" }
        };
        var tail = CreateToast(withErrorPort: false) with { NodeId = tailId };

        var result = Validator.Validate(def with
        {
            Nodes = [.. def.Nodes, pfe, tail],
            Edges = [.. def.Edges, new Edge((pfeId, "body"), (tailId, "in"))]
        });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "PARALLEL_FOREACH_WITHOUT_JOIN" && i.NodeId == pfeId);
    }

    [Fact]
    public void Validate_accepts_a_ParallelForEach_that_converges_on_a_Join()
    {
        var def = ValidWorkflowBuilder.Build();
        var pfeId = Guid.NewGuid();
        var joinId = Guid.NewGuid();
        var pfe = new ParallelForEachNode
        {
            NodeId = pfeId,
            Name = "pfe",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" },
                new PortDefinition { PortId = "empty", Direction = PortDirection.Output, Label = "Empty" }
            ],
            Config = new ParallelForEachConfig { Collection = "items" }
        };
        var join = new JoinNode
        {
            NodeId = joinId,
            Name = "join",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
            ],
            Config = new JoinConfig { Mode = JoinMode.All }
        };

        var result = Validator.Validate(def with
        {
            Nodes = [.. def.Nodes, pfe, join],
            Edges = [.. def.Edges, new Edge((pfeId, "body"), (joinId, "in"))]
        });

        Assert.DoesNotContain(result.Issues, i => i.Code == "PARALLEL_FOREACH_WITHOUT_JOIN");
    }

    [Fact]
    public void Validate_does_not_require_a_Join_after_a_Fork()
    {
        // A Fork without a Join is legal - its branches simply run to their own ends.
        var def = ValidWorkflowBuilder.Build();
        var forkId = Guid.NewGuid();
        var fork = new ForkNode
        {
            NodeId = forkId,
            Name = "fork",
            Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "a", Direction = PortDirection.Output, Label = "a" },
                new PortDefinition { PortId = "b", Direction = PortDirection.Output, Label = "b" }
            ],
            Config = new ForkConfig { Branches = ["a", "b"] }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, fork] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "PARALLEL_FOREACH_WITHOUT_JOIN");
    }

    // --------------------------------------------- WF-19.5/.6 trigger config

    [Fact]
    public void Validate_rejects_an_invalid_cron_expression()
    {
        // Previously this threw only AFTER the definition row was inserted, leaving a published
        // workflow that could never fire.
        var def = ValidWorkflowBuilder.Build();
        var schedule = new ScheduleTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "sched",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ScheduleTriggerConfig { Cron = "not a cron" }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, schedule] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_CRON");
    }

    [Theory]
    [InlineData("0 6 * * *")]      // 5-field
    [InlineData("0 0 6 * * *")]    // 6-field with seconds
    public void Validate_accepts_both_supported_cron_formats(string cron)
    {
        var def = ValidWorkflowBuilder.Build();
        var schedule = new ScheduleTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "sched",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ScheduleTriggerConfig { Cron = cron }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, schedule] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "INVALID_CRON");
    }

    [Fact]
    public void Validate_warns_when_a_concurrency_policy_has_no_correlation_key()
    {
        var def = ValidWorkflowBuilder.Build();
        var trigger = new ClientTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "t",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientTriggerConfig
            {
                ClientRef = "dev-1",
                Type = "telemetry",
                CorrelationKey = null,
                ConcurrencyPolicy = WorkflowConcurrencyPolicy.CancelExisting
            }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, trigger] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "CONCURRENCY_POLICY_WITHOUT_CORRELATION_KEY");
    }

    private static ToastNotificationNode CreateToast(bool withErrorPort)
    {
        List<PortDefinition> ports =
        [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
        ];

        if (withErrorPort)
        {
            ports.Add(new PortDefinition { PortId = "error", Direction = PortDirection.Output, Label = "Error" });
        }

        return new ToastNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "toast",
            Ports = ports,
            Config = new ToastConfig { Title = "t", Message = "m" }
        };
    }
}

public static class ValidWorkflowBuilder
{
    public static readonly Guid Trigger1Id = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Logic1Id = new("aaaaaaaa-0000-0000-0000-000000000002");

    public static WorkflowDefinition Build()
    {
        var trigger = new ClientTriggerNode { NodeId = Trigger1Id, Name = "Trigger", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientTriggerConfig { ClientRef = "dev-1", Type = "telemetry" } };

        var logic = new LogicGateNode { NodeId = Logic1Id, Name = "Gate", Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" },
                new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }
            ], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("$trigger.temperature > 35") } };

        var edge = new Edge((Trigger1Id, "out"), (Logic1Id, "in"));

        return new WorkflowDefinition(
            WorkflowRefId: Guid.NewGuid(),
            Version: 1,
            WorkspaceId: 1,
            Name: "Test Workflow",
            Description: null,
            IsEnabled: true,
            Nodes: [trigger, logic],
            Edges: [edge],
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1);
    }
}

