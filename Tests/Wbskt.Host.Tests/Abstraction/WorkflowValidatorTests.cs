using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

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

    [Theory]
    [InlineData("sys.ping")]
    [InlineData("capabilities")]
    [InlineData("state.report")]
    [InlineData("")]
    public void Validate_refuses_a_client_message_the_console_would_refuse(string type)
    {
        var def = ValidWorkflowBuilder.Build();
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, CreateClientMessage(type)] });
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "CLIENT_MESSAGE_INVALID");
    }

    [Fact]
    public void Validate_allows_an_ordinary_client_message()
    {
        var def = ValidWorkflowBuilder.Build();
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, CreateClientMessage("OpenVent")] });
        Assert.DoesNotContain(result.Issues, i => i.Code == "CLIENT_MESSAGE_INVALID");
    }

    [Fact]
    public void A_client_message_waiting_for_its_ack_leaves_by_acked_failed_and_timeout_not_default()
    {
        var def = ValidWorkflowBuilder.Build();
        SendClientMessageNode waiting = CreateClientMessage("OpenVent") with
        {
            Config = new SendClientMessageConfig { ClientRef = Guid.NewGuid().ToString(), Type = "OpenVent", AwaitAck = true }
        };

        var withDefaultPort = Validator.Validate(def with { Nodes = [.. def.Nodes, waiting] });
        var withAckPorts = Validator.Validate(def with { Nodes = [.. def.Nodes, waiting with
        {
            Ports =
            [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "acked", Direction = PortDirection.Output, Label = "Acked" },
                new PortDefinition { PortId = "failed", Direction = PortDirection.Output, Label = "Failed" },
                new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }
            ]
        }] });

        Assert.Contains(withDefaultPort.Issues, i => i.Code == "MISSING_PORT");
        Assert.Contains(withDefaultPort.Issues, i => i.Code == "EXTRA_PORT");
        Assert.DoesNotContain(withAckPorts.Issues, i => i.Code is "MISSING_PORT" or "EXTRA_PORT");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(25 * 3600)]
    public void Validate_refuses_an_ack_timeout_that_is_not_positive_or_over_a_day(int seconds)
    {
        var def = ValidWorkflowBuilder.Build();
        SendClientMessageNode node = CreateClientMessage("OpenVent") with
        {
            Config = new SendClientMessageConfig { ClientRef = Guid.NewGuid().ToString(), Type = "OpenVent", AwaitAck = true, AckTimeout = TimeSpan.FromSeconds(seconds) }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, node] });

        Assert.Contains(result.Issues, i => i.Code == "CLIENT_MESSAGE_ACK_TIMEOUT");
    }

    private static SendClientMessageNode CreateClientMessage(string type) => new()
    {
        NodeId = new Guid("aaaaaaaa-0000-0000-0000-00000000009c"),
        Name = "send",
        Ports =
        [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }
        ],
        Config = new SendClientMessageConfig { ClientRef = Guid.NewGuid().ToString(), Type = type }
    };

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
    public void Validate_accepts_email_now_that_it_has_an_executor()
    {
        // action:email used to publish cleanly and then fail the run at execution, so the validator
        // rejected it until its executor landed. It must now publish with no kind error.
        var def = ValidWorkflowBuilder.Build();
        var email = new EmailNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "mail",
            Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new EmailConfig { To = "a@b", Subject = "s", Body = "b" }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, email] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "UNKNOWN_NODE_KIND");
    }

    [Fact]
    public void Validate_rejects_a_kind_with_no_descriptor()
    {
        // A kind missing from the descriptor list has no port table, no price and no executor. It used
        // to be validated with no port check at all; now it is an error, and only that one error - its
        // ports are not also each reported as unexpected.
        var def = ValidWorkflowBuilder.Build();
        var unknown = new UndescribedNode
        {
            NodeId = Guid.NewGuid(),
            Name = "mystery",
            Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "whatever", Direction = PortDirection.Output, Label = "Out" }]
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, unknown] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "UNKNOWN_NODE_KIND" && i.NodeId == unknown.NodeId);
        Assert.DoesNotContain(result.Issues, i => i.Code is "EXTRA_PORT" or "MISSING_PORT");
    }

    private sealed record UndescribedNode : BaseNode
    {
        public override string Kind => "action:undescribed";
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

        Assert.DoesNotContain(result.Issues, i => i.Code == "UNKNOWN_NODE_KIND");
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

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_rejects_a_retry_factor_that_is_not_a_positive_number(double factor)
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with { Retry = Retry(factor: factor, maxDelay: null) };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "RETRY_FACTOR_INVALID" && i.NodeId == action.NodeId);
    }

    [Fact]
    public void Validate_rejects_a_retry_max_delay_below_the_initial_delay()
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with { Retry = Retry(factor: null, maxDelay: TimeSpan.FromMilliseconds(500)) };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "RETRY_MAX_DELAY_BELOW_INITIAL" && i.NodeId == action.NodeId);
    }

    [Fact]
    public void Validate_accepts_a_retry_policy_with_a_factor_and_a_ceiling()
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with { Retry = Retry(factor: 3.0, maxDelay: TimeSpan.FromSeconds(30)) };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        Assert.DoesNotContain(result.Issues, i => i.Code is "RETRY_FACTOR_INVALID" or "RETRY_MAX_DELAY_BELOW_INITIAL");
    }

    private static RetryPolicy Retry(double? factor, TimeSpan? maxDelay) => new()
    {
        Strategy = RetryStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        Factor = factor,
        MaxDelay = maxDelay,
        MaxAttempts = 3,
        JitterPct = 0,
        RetryOn = []
    };

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

    [Fact]
    public void Validate_rejects_an_onFailure_of_Compensate()
    {
        var def = ValidWorkflowBuilder.Build();
        var action = CreateToast(withErrorPort: false) with
        {
            OnFailure = new OnFailureConfig { Outcome = ErrorOutcome.Compensate }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, action] });

        var issue = Assert.Single(result.Issues, i => i.Code == "COMPENSATION_NOT_SUPPORTED");
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(action.NodeId, issue.NodeId);
        Assert.Contains("not available in this release", issue.Message);
    }

    [Fact]
    public void Validate_rejects_runCompensationOnFailure()
    {
        var def = ValidWorkflowBuilder.Build() with { RunCompensationOnFailure = true };

        var result = Validator.Validate(def);

        var issue = Assert.Single(result.Issues, i => i.Code == "COMPENSATION_NOT_SUPPORTED");
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Null(issue.NodeId);
        Assert.Contains("not available in this release", issue.Message);
    }

    [Fact]
    public void Validate_does_not_report_compensation_on_a_definition_that_does_not_ask_for_it()
    {
        var result = Validator.Validate(ValidWorkflowBuilder.Build());

        Assert.DoesNotContain(result.Issues, i => i.Code == "COMPENSATION_NOT_SUPPORTED");
    }

    // ---------------------------------------------- WF-09 sub-workflow inputs

    [Theory]
    [InlineData("parentRunRefId")]
    [InlineData("correlationKey")]
    [InlineData("depth")]
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

    [Fact]
    public void Validate_rejects_an_End_output_on_a_workflow_no_parent_can_start()
    {
        // A parent starts its child through the child's manual trigger, so on a workflow without one the
        // output could never reach anyone.
        var def = ValidWorkflowBuilder.Build();
        EndNode end = EndWithOutput();

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, end] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "END_OUTPUT_WITHOUT_MANUAL_TRIGGER" && i.NodeId == end.NodeId);
    }

    [Fact]
    public void Validate_accepts_an_End_output_on_a_workflow_with_a_manual_trigger()
    {
        var def = ValidWorkflowBuilder.Build();
        var manual = new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "run", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ManualTriggerConfig { Description = "run" } };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, manual, EndWithOutput()] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "END_OUTPUT_WITHOUT_MANUAL_TRIGGER");
    }

    private static EndNode EndWithOutput() => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "End",
        Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }],
        Config = new EndConfig { Output = new BranchStateRefExpression("total") }
    };

    [Fact]
    public void Validate_rejects_a_SubWorkflow_that_starts_its_own_workflow()
    {
        // A child is exempt from the workspace run ceiling, so a workflow that starts itself would start
        // children without end.
        var def = ValidWorkflowBuilder.Build();
        SubWorkflowNode sub = SubWorkflowTo(def.WorkflowRefId);

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, sub] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "SUBWORKFLOW_SELF_REFERENCE" && i.NodeId == sub.NodeId);
    }

    [Fact]
    public void Validate_rejects_a_SubWorkflow_that_starts_the_workflow_it_is_published_under()
    {
        // Publishing stores the definition under the request's id, which the definition's own
        // WorkflowRefId is not required to match.
        var def = ValidWorkflowBuilder.Build();
        var publishedAs = Guid.NewGuid();
        SubWorkflowNode sub = SubWorkflowTo(publishedAs);

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, sub] }, publishedAs);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "SUBWORKFLOW_SELF_REFERENCE" && i.NodeId == sub.NodeId);
    }

    [Fact]
    public void Validate_accepts_a_SubWorkflow_that_starts_another_workflow()
    {
        var def = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, SubWorkflowTo(Guid.NewGuid())] }, Guid.NewGuid());

        Assert.DoesNotContain(result.Issues, i => i.Code == "SUBWORKFLOW_SELF_REFERENCE");
    }

    // ---------------------------------------------- local variable names

    [Theory]
    [InlineData("trigger")]
    [InlineData("__join_token")]
    [InlineData("__wake")]
    [InlineData("__anything_new")]
    [InlineData("joinContributed")]
    public void Validate_rejects_a_Local_variable_named_after_an_engine_key(string name)
    {
        // A Local variable is written straight into branch state, where the engine keeps the trigger
        // payload and its own bookkeeping; one named __join_token would strand its cohort at the Join.
        var def = ValidWorkflowBuilder.Build();
        VariableNode variable = CreateVariable(VariableScope.Local, name);

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, variable] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "VARIABLE_NAME_RESERVED" && i.NodeId == variable.NodeId);
    }

    [Theory]
    [InlineData(VariableScope.Local, "orderTotal")]
    [InlineData(VariableScope.Local, "triggered")]
    [InlineData(VariableScope.Shared, "__counter")]
    public void Validate_accepts_variable_names_that_cannot_reach_engine_state(VariableScope scope, string name)
    {
        // Shared variables live in their own table, so only a Local name can collide.
        var def = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, CreateVariable(scope, name)] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "VARIABLE_NAME_RESERVED");
    }

    private static VariableNode CreateVariable(VariableScope scope, string name) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "set",
        Ports = [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
        ],
        Config = new VariableConfig { Scope = scope, Op = VariableOperation.Set, Var = name, Value = System.Text.Json.JsonSerializer.SerializeToElement(1) }
    };

    private static SubWorkflowNode SubWorkflowTo(Guid workflowRefId) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "sub",
        Ports = [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
        ],
        Config = new SubWorkflowConfig { WorkflowRefId = workflowRefId }
    };

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

    // ----------------------------------------------- Joins without a cohort

    [Fact]
    public void Validate_rejects_a_Join_placed_after_a_Forks_Join()
    {
        // The first Join's winner carries no token on, so the second was reached by a branch with
        // no cohort to count it in: the engine fails it with JOIN_NO_TOKEN.
        var forkId = Guid.NewGuid();
        var joinId = Guid.NewGuid();
        var secondJoinId = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinId), Join(secondJoinId)],
            [
                new Edge((forkId, "a"), (joinId, "in")),
                new Edge((forkId, "b"), (joinId, "in")),
                new Edge((joinId, "default"), (secondJoinId, "in"))
            ]);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "JOIN_WITHOUT_COHORT" && i.NodeId == secondJoinId);
        Assert.DoesNotContain(result.Issues, i => i.Code == "JOIN_WITHOUT_COHORT" && i.NodeId == joinId);
    }

    [Fact]
    public void Validate_rejects_a_Join_that_no_fan_out_feeds()
    {
        var gateId = Guid.NewGuid();
        var joinId = Guid.NewGuid();

        var result = ValidateGraph(
            [Gate(gateId), Join(joinId)],
            [
                new Edge((gateId, "true"), (joinId, "in")),
                new Edge((gateId, "false"), (joinId, "in"))
            ]);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "JOIN_WITHOUT_COHORT" && i.NodeId == joinId);
    }

    [Fact]
    public void Validate_accepts_the_Joins_of_a_Fork_nested_in_another_Forks_branch()
    {
        var outerFork = Guid.NewGuid();
        var innerFork = Guid.NewGuid();
        var innerJoin = Guid.NewGuid();
        var outerJoin = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(outerFork, "a", "b"), Fork(innerFork, "x", "y"), Join(innerJoin), Join(outerJoin)],
            [
                new Edge((outerFork, "a"), (innerFork, "in")),
                new Edge((innerFork, "x"), (innerJoin, "in")),
                new Edge((innerFork, "y"), (innerJoin, "in")),
                new Edge((innerJoin, "default"), (outerJoin, "in")),
                new Edge((outerFork, "b"), (outerJoin, "in"))
            ]);

        Assert.DoesNotContain(result.Issues, i => i.Code == "JOIN_WITHOUT_COHORT");
    }

    // ----------------------------------------------- Fork/Join pairing

    [Fact]
    public void Validate_rejects_a_Fork_with_a_branch_straight_to_End()
    {
        // The branch that ends never counts at the Join, so a Mode=All join never converged and the
        // run finalized Succeeded with everything after the Join skipped.
        Guid forkId = Guid.NewGuid(), joinId = Guid.NewGuid(), endId = Guid.NewGuid(), stepId = Guid.NewGuid();
        var step = CreateToast(withErrorPort: false) with { NodeId = stepId };

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinId), End(endId), step],
            [
                new Edge((forkId, "a"), (stepId, "in")),
                new Edge((stepId, "default"), (joinId, "in")),
                new Edge((forkId, "b"), (endId, "in"))
            ]);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "FORK_BRANCH_WITHOUT_JOIN" && i.NodeId == forkId);
    }

    [Fact]
    public void Validate_accepts_a_Fork_without_a_Join_as_a_fire_and_forget_fan_out()
    {
        // No branch reaches a Join, so there is no join to converge: the branches run to their own
        // ends and the run ends when they all do.
        Guid forkId = Guid.NewGuid(), stepA = Guid.NewGuid(), endId = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), CreateToast(withErrorPort: false) with { NodeId = stepA }, End(endId)],
            [new Edge((forkId, "a"), (stepA, "in")), new Edge((forkId, "b"), (endId, "in"))]);

        Assert.DoesNotContain(result.Issues, i => i.Code is "FORK_BRANCH_WITHOUT_JOIN" or "FORK_BRANCHES_JOIN_MISMATCH" or "PARALLEL_FOREACH_WITHOUT_JOIN");
    }

    [Fact]
    public void Validate_accepts_a_Fork_branch_that_fails_the_run_instead_of_reaching_the_Join()
    {
        // FailRun fails the whole run, so the join never needs to converge.
        Guid forkId = Guid.NewGuid(), joinId = Guid.NewGuid(), gateId = Guid.NewGuid(), failId = Guid.NewGuid();
        var failRun = new FailRunNode
        {
            NodeId = failId,
            Name = "fail",
            Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }],
            Config = new FailRunConfig { Reason = "nope" }
        };

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinId), Gate(gateId), failRun],
            [
                new Edge((forkId, "a"), (gateId, "in")),
                new Edge((gateId, "true"), (joinId, "in")),
                new Edge((gateId, "false"), (failId, "in")),
                new Edge((forkId, "b"), (joinId, "in"))
            ]);

        Assert.DoesNotContain(result.Issues, i => i.Code is "FORK_BRANCH_WITHOUT_JOIN" or "FORK_BRANCHES_JOIN_MISMATCH");
    }

    [Fact]
    public void Validate_rejects_a_Fork_branch_that_a_Logic_port_with_no_edge_ends()
    {
        Guid forkId = Guid.NewGuid(), joinId = Guid.NewGuid(), gateId = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinId), Gate(gateId)],
            [
                new Edge((forkId, "a"), (gateId, "in")),
                new Edge((gateId, "true"), (joinId, "in")),
                new Edge((forkId, "b"), (joinId, "in"))
            ]);

        Assert.Contains(result.Issues, i => i.Code == "FORK_BRANCH_WITHOUT_JOIN" && i.NodeId == forkId);
    }

    [Fact]
    public void Validate_rejects_a_Fork_whose_branches_reach_different_Joins()
    {
        Guid forkId = Guid.NewGuid(), joinA = Guid.NewGuid(), joinB = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinA), Join(joinB)],
            [new Edge((forkId, "a"), (joinA, "in")), new Edge((forkId, "b"), (joinB, "in"))]);

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "FORK_BRANCHES_JOIN_MISMATCH" && i.NodeId == forkId);
        Assert.DoesNotContain(result.Issues, i => i.Code == "FORK_BRANCH_WITHOUT_JOIN");
    }

    [Fact]
    public void Validate_accepts_a_Fork_whose_branches_converge_on_one_Join()
    {
        Guid forkId = Guid.NewGuid(), joinId = Guid.NewGuid(), stepId = Guid.NewGuid(), endId = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(forkId, "a", "b"), Join(joinId), CreateToast(withErrorPort: false) with { NodeId = stepId }, End(endId)],
            [
                new Edge((forkId, "a"), (stepId, "in")),
                new Edge((stepId, "default"), (joinId, "in")),
                new Edge((forkId, "b"), (joinId, "in")),
                // What comes after the Join may end however it likes.
                new Edge((joinId, "default"), (endId, "in"))
            ]);

        Assert.DoesNotContain(result.Issues, i => i.Code is "FORK_BRANCH_WITHOUT_JOIN" or "FORK_BRANCHES_JOIN_MISMATCH");
    }

    [Fact]
    public void Validate_accepts_a_nested_Fork_that_converges_and_continues_to_the_outer_Join()
    {
        // outer fork: a -> inner fork (x, y -> inner join -> outer join), b -> outer join
        Guid outerFork = Guid.NewGuid(), innerFork = Guid.NewGuid(), innerJoin = Guid.NewGuid(), outerJoin = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(outerFork, "a", "b"), Fork(innerFork, "x", "y"), Join(innerJoin), Join(outerJoin)],
            [
                new Edge((outerFork, "a"), (innerFork, "in")),
                new Edge((innerFork, "x"), (innerJoin, "in")),
                new Edge((innerFork, "y"), (innerJoin, "in")),
                new Edge((innerJoin, "default"), (outerJoin, "in")),
                new Edge((outerFork, "b"), (outerJoin, "in"))
            ]);

        Assert.DoesNotContain(result.Issues, i => i.Code is "FORK_BRANCH_WITHOUT_JOIN" or "FORK_BRANCHES_JOIN_MISMATCH");
    }

    [Fact]
    public void Validate_rejects_an_outer_Fork_whose_nested_cohort_never_continues_to_the_outer_Join()
    {
        // The inner cohort converges, but its continuation ends instead of reaching the outer Join.
        Guid outerFork = Guid.NewGuid(), innerFork = Guid.NewGuid(), innerJoin = Guid.NewGuid(), outerJoin = Guid.NewGuid(), endId = Guid.NewGuid();

        var result = ValidateGraph(
            [Fork(outerFork, "a", "b"), Fork(innerFork, "x", "y"), Join(innerJoin), Join(outerJoin), End(endId)],
            [
                new Edge((outerFork, "a"), (innerFork, "in")),
                new Edge((innerFork, "x"), (innerJoin, "in")),
                new Edge((innerFork, "y"), (innerJoin, "in")),
                new Edge((innerJoin, "default"), (endId, "in")),
                new Edge((outerFork, "b"), (outerJoin, "in"))
            ]);

        Assert.Contains(result.Issues, i => i.Code == "FORK_BRANCH_WITHOUT_JOIN" && i.NodeId == outerFork);
        Assert.DoesNotContain(result.Issues, i => i.NodeId == innerFork && i.Code is "FORK_BRANCH_WITHOUT_JOIN" or "FORK_BRANCHES_JOIN_MISMATCH");
    }

    [Fact]
    public void FindJoinForCohort_returns_the_outer_Join_for_a_Fork_with_a_nested_Fork()
    {
        // The nearest Join downstream of the outer Fork is the inner one; the cohort's own is the outer.
        Guid outerFork = Guid.NewGuid(), innerFork = Guid.NewGuid(), innerJoin = Guid.NewGuid(), outerJoin = Guid.NewGuid();
        var def = ValidWorkflowBuilder.Build() with
        {
            Nodes = [Fork(outerFork, "a", "b"), Fork(innerFork, "x", "y"), Join(innerJoin), Join(outerJoin)],
            Edges = [
                new Edge((outerFork, "a"), (innerFork, "in")),
                new Edge((innerFork, "x"), (innerJoin, "in")),
                new Edge((innerFork, "y"), (innerJoin, "in")),
                new Edge((innerJoin, "default"), (outerJoin, "in")),
                new Edge((outerFork, "b"), (outerJoin, "in"))
            ]
        };

        Assert.Equal(outerJoin, WorkflowGraph.FindJoinForCohort(def, outerFork, ["a", "b"])?.NodeId);
        Assert.Equal(innerJoin, WorkflowGraph.FindJoinForCohort(def, innerFork, ["x", "y"])?.NodeId);
    }

    private static ValidationResult ValidateGraph(IReadOnlyCollection<BaseNode> nodes, IReadOnlyCollection<Edge> edges)
    {
        var def = ValidWorkflowBuilder.Build();
        return Validator.Validate(def with { Nodes = [.. def.Nodes, .. nodes], Edges = [.. def.Edges, .. edges] });
    }

    private static ForkNode Fork(Guid nodeId, params string[] branches) => new()
    {
        NodeId = nodeId,
        Name = "fork",
        Ports = [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            .. branches.Select(branch => new PortDefinition { PortId = branch, Direction = PortDirection.Output, Label = branch })
        ],
        Config = new ForkConfig { Branches = [.. branches] }
    };

    private static LogicGateNode Gate(Guid nodeId) => new()
    {
        NodeId = nodeId,
        Name = "gate",
        Ports = [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" },
            new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }
        ],
        Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("$trigger.temperature > 35") }
    };

    private static JoinNode Join(Guid nodeId, JoinMode mode = JoinMode.All) => new()
    {
        NodeId = nodeId,
        Name = "join",
        Ports = [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
        ],
        Config = new JoinConfig { Mode = mode }
    };

    private static EndNode End(Guid nodeId) => new()
    {
        NodeId = nodeId,
        Name = "end",
        Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }]
    };

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
    [InlineData("sensor-A", 60, "INVALID_CLIENT_REF")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", -1, "INVALID_PRESENCE_DURATION")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 86401, "INVALID_PRESENCE_DURATION")]
    public void Validate_rejects_a_presence_trigger_that_could_never_fire(string clientRef, int forSeconds, string expectedCode)
    {
        var def = ValidWorkflowBuilder.Build();
        var presence = new ClientPresenceTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "offline",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientPresenceTriggerConfig { ClientRef = clientRef, ForSeconds = forSeconds }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, presence] });

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == expectedCode);
    }

    [Theory]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 600, false, "HOLD_REQUIRES_FILTER")]
    [InlineData("sensor-A", 600, true, "INVALID_CLIENT_REF")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", -1, true, "INVALID_HOLD_DURATION")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 86401, true, "INVALID_HOLD_DURATION")]
    public void Validate_rejects_a_bad_client_hold(string clientRef, int holdSeconds, bool withFilter, string expectedCode)
    {
        var result = Validator.Validate(WithClientTrigger(clientRef, holdSeconds, withFilter));

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == expectedCode);
    }

    [Theory]
    [InlineData("sensor-A", 0, false)]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 600, true)]
    public void Validate_accepts_a_client_trigger_without_a_hold_or_with_a_filtered_one(string clientRef, int holdSeconds, bool withFilter)
    {
        var result = Validator.Validate(WithClientTrigger(clientRef, holdSeconds, withFilter));

        Assert.DoesNotContain(result.Issues, i => i.Code is "HOLD_REQUIRES_FILTER" or "INVALID_CLIENT_REF" or "INVALID_HOLD_DURATION");
    }

    [Fact]
    public void Validate_rejects_a_hold_on_any_message_type()
    {
        var result = Validator.Validate(WithClientTrigger("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 600, true, type: "*"));

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "HOLD_REQUIRES_MESSAGE_TYPE");
    }

    [Fact]
    public void Validate_accepts_any_message_type_without_a_hold()
    {
        var result = Validator.Validate(WithClientTrigger("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 0, true, type: "*"));

        Assert.DoesNotContain(result.Issues, i => i.Code == "HOLD_REQUIRES_MESSAGE_TYPE");
    }

    [Theory]
    [InlineData("{{payload.room}}")]
    [InlineData("room-{{trigger.payload.room}}")]
    [InlineData("{{trigger.payload.room}} {{trigger.payload.floor}}")]
    public void Validate_rejects_a_correlation_template_that_is_not_a_trigger_path(string correlationKey)
    {
        var result = Validator.Validate(WithClientTrigger("sensor-A", 0, false, correlationKey: correlationKey));

        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_CORRELATION_KEY");
    }

    [Theory]
    [InlineData("{{trigger.payload.room}}")]
    [InlineData("{{ trigger.payload.room }}")]
    [InlineData("$trigger.payload.room")]
    [InlineData("greenhouse")]
    public void Validate_accepts_a_trigger_path_or_constant_correlation_key(string correlationKey)
    {
        var result = Validator.Validate(WithClientTrigger("sensor-A", 0, false, correlationKey: correlationKey));

        Assert.DoesNotContain(result.Issues, i => i.Code is "INVALID_CORRELATION_KEY" or "SUSPICIOUS_CORRELATION_KEY");
    }

    private static WorkflowDefinition WithClientTrigger(string clientRef, int holdSeconds, bool withFilter, string type = "temperature", string? correlationKey = null)
    {
        var def = ValidWorkflowBuilder.Build();
        var client = new ClientTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "too warm",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientTriggerConfig
            {
                ClientRef = clientRef,
                Type = type,
                CorrelationKey = correlationKey,
                HoldSeconds = holdSeconds,
                Filter = withFilter ? new LiteralExpression(true) : null
            }
        };
        return def with { Nodes = [client], Edges = [] };
    }

    [Fact]
    public void Validate_accepts_a_presence_trigger_and_counts_it_as_a_trigger()
    {
        var def = ValidWorkflowBuilder.Build();
        var presence = new ClientPresenceTriggerNode
        {
            NodeId = Guid.NewGuid(),
            Name = "offline",
            Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }],
            Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientPresenceTriggerConfig { ClientRef = Guid.NewGuid().ToString(), ForSeconds = 0 }
        };

        var result = Validator.Validate(def with { Nodes = [presence] , Edges = [] });

        Assert.DoesNotContain(result.Issues, i => i.Code is "INVALID_CLIENT_REF" or "INVALID_PRESENCE_DURATION" or "NO_TRIGGERS");
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

    // With no key, the dispatcher groups runs by the channel's own default (device and message type
    // here), so a policy still applies: there is nothing to warn about.
    [Theory]
    [InlineData(WorkflowConcurrencyPolicy.Queue)]
    [InlineData(WorkflowConcurrencyPolicy.CancelExisting)]
    [InlineData(WorkflowConcurrencyPolicy.DropIfRunning)]
    public void Validate_does_not_warn_when_a_concurrency_policy_has_no_correlation_key(WorkflowConcurrencyPolicy policy)
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
                ConcurrencyPolicy = policy
            }
        };

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, trigger] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "CONCURRENCY_POLICY_WITHOUT_CORRELATION_KEY");
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

