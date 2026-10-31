using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>
/// What is filled in on each step: a blank or impossible setting is a problem with the step and the
/// field to fix, listed with every other problem rather than failing the request or the run.
/// </summary>
public sealed class StepSettingsValidationTests
{
    private static readonly WorkflowValidator Validator = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_device_step_with_no_command_asks_for_one_on_the_type_field()
    {
        var issue = Assert.Single(Errors(Send(Guid.NewGuid().ToString(), "")));

        Assert.Equal("COMMAND_REQUIRED", issue.Code);
        Assert.Equal("config.type", issue.Field);
    }

    [Theory]
    [InlineData("", "CLIENT_REF_REQUIRED")]
    [InlineData("$trigger.deviceId", "CLIENT_REF_INVALID")]
    public void A_device_step_must_name_a_device_by_id(string clientRef, string code)
    {
        var issue = Assert.Single(Errors(Send(clientRef, "led.set")));

        Assert.Equal(code, issue.Code);
        Assert.Equal("config.clientRef", issue.Field);
    }

    [Fact]
    public void A_reserved_command_names_the_type_field()
    {
        var issue = Assert.Single(Errors(Send(Guid.NewGuid().ToString(), "sys.ping")));

        Assert.Equal("CLIENT_MESSAGE_INVALID", issue.Code);
        Assert.Equal("config.type", issue.Field);
    }

    [Fact]
    public void Settings_left_out_of_the_json_are_listed_together_instead_of_failing_the_request()
    {
        // The designer sends what the author filled in. Both blanks come back at once, each on its field.
        var node = JsonSerializer.Deserialize<BaseNode>(
            """{ "nodeId": "00000000-0000-0000-0000-000000000021", "kind": "action:email", "name": "Mail", "ports": [], "config": { "body": "hi" } }""",
            Options)!;

        var issues = Errors(node).Where(i => i.Code.StartsWith("EMAIL_", StringComparison.Ordinal));

        Assert.Equal(["config.to", "config.subject"], issues.Select(i => i.Field));
    }

    [Theory]
    [InlineData("", "WEBHOOK_URL_MISSING", "config.url")]
    [InlineData("ftp://example.test/file", "WEBHOOK_URL_INVALID", "config.url")]
    public void Call_a_url_needs_an_http_url(string url, string code, string field)
    {
        var issue = Assert.Single(Errors(Webhook(url, "POST")));

        Assert.Equal(code, issue.Code);
        Assert.Equal(field, issue.Field);
    }

    [Fact]
    public void Call_a_url_needs_a_known_method()
    {
        Assert.Equal("WEBHOOK_METHOD_INVALID", Assert.Single(Errors(Webhook("https://example.test", "FETCH"))).Code);
        Assert.Empty(Errors(Webhook("https://example.test", "post")));
    }

    [Fact]
    public void A_variable_step_must_name_its_variable()
    {
        var issue = Assert.Single(Errors(Variable(VariableScope.Local, "", JsonSerializer.SerializeToElement(1))));

        Assert.Equal("VARIABLE_NAME_MISSING", issue.Code);
        Assert.Equal("config.var", issue.Field);
    }

    [Fact]
    public void A_variable_value_that_is_an_unreadable_expression_is_reported_on_its_field()
    {
        var value = JsonSerializer.SerializeToElement(new { kind = "nonsense" });

        var issue = Assert.Single(Errors(Variable(VariableScope.Local, "count", value)));

        Assert.Equal("EXPRESSION_INVALID", issue.Code);
        Assert.Equal("config.value", issue.Field);
    }

    [Fact]
    public void A_variable_value_with_a_broken_template_is_reported()
    {
        var value = JsonSerializer.SerializeToElement<WorkflowExpression>(new TemplateExpression("{{trigger.payload.room"), Options);

        Assert.Equal("EXPRESSION_INVALID", Assert.Single(Errors(Variable(VariableScope.Local, "room", value))).Code);
    }

    [Fact]
    public void A_plain_variable_value_is_not_an_expression()
    {
        Assert.Empty(Errors(Variable(VariableScope.Local, "note", JsonSerializer.SerializeToElement(new { kind = 3 }))));
    }

    [Fact]
    public void A_logic_condition_that_reads_nothing_is_reported()
    {
        var gate = Gate(new BinaryExpression(new BranchStateRefExpression(""), BinaryOperator.GreaterThan, new LiteralExpression(30)));

        var issue = Assert.Single(Errors(gate));

        Assert.Equal("EXPRESSION_INVALID", issue.Code);
        Assert.Equal("config.condition", issue.Field);
    }

    [Theory]
    [InlineData(-1, "DELAY_DURATION_INVALID")]
    [InlineData(366 * 24 * 60, "DELAY_DURATION_INVALID")]
    public void A_delay_must_be_in_range(int minutes, string code)
    {
        Assert.Equal(code, Assert.Single(Errors(Delay(TimeSpan.FromMinutes(minutes)))).Code);
    }

    [Fact]
    public void A_zero_delay_is_a_warning()
    {
        var node = Delay(TimeSpan.Zero);

        Assert.Empty(Errors(node));
        Assert.Contains(IssuesFor(node), i => i.Code == "DELAY_DURATION_ZERO" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void A_loop_and_a_sub_workflow_must_say_what_they_work_on()
    {
        var loop = new ForEachNode { NodeId = Guid.NewGuid(), Name = "each", Ports = [], Config = new ForEachConfig { Collection = " " } };
        var sub = new SubWorkflowNode { NodeId = Guid.NewGuid(), Name = "child", Ports = [], Config = new SubWorkflowConfig { WorkflowRefId = Guid.Empty } };

        Assert.Equal("FOREACH_COLLECTION_MISSING", Assert.Single(Errors(loop)).Code);
        Assert.Equal("SUBWORKFLOW_TARGET_MISSING", Assert.Single(Errors(sub)).Code);
    }

    [Fact]
    public void A_fork_branch_named_twice_is_reported()
    {
        var fork = new ForkNode { NodeId = Guid.NewGuid(), Name = "split", Ports = [], Config = new ForkConfig { Branches = ["a", "a"] } };

        Assert.Contains(Errors(fork), i => i.Code == "FORK_BRANCH_DUPLICATE");
    }

    [Fact]
    public void A_shared_variable_no_step_sets_is_a_warning_on_the_field_that_reads_it()
    {
        var toast = Toast("Count is {{$shared.cuont}}");

        var issue = Assert.Single(IssuesFor(toast), i => i.Code == "SHARED_VARIABLE_UNDECLARED");

        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal("config.title", issue.Field);
    }

    [Fact]
    public void A_shared_variable_this_workflow_sets_is_declared()
    {
        var toast = Toast("Count is {{$shared.count}}");
        var set = Variable(VariableScope.Shared, "count", JsonSerializer.SerializeToElement(1));
        var def = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, toast, set] });

        Assert.DoesNotContain(result.Issues, i => i.Code == "SHARED_VARIABLE_UNDECLARED");
    }

    [Fact]
    public void An_error_port_that_leads_nowhere_is_a_warning()
    {
        var toast = Toast("t") with
        {
            Ports = [new PortDefinition { PortId = "error", Direction = PortDirection.Output, Label = "Error" }]
        };

        var issue = Assert.Single(IssuesFor(toast), i => i.Code == "ERROR_PATH_ENDS");

        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void An_error_port_that_leads_to_a_step_is_fine()
    {
        var toast = Toast("t") with
        {
            Ports = [new PortDefinition { PortId = "error", Direction = PortDirection.Output, Label = "Error" }]
        };
        var next = Toast("handled");
        var def = ValidWorkflowBuilder.Build();

        var result = Validator.Validate(def with
        {
            Nodes = [.. def.Nodes, toast, next],
            Edges = [.. def.Edges, new Edge((toast.NodeId, "error"), (next.NodeId, "in"))]
        });

        Assert.DoesNotContain(result.Issues, i => i.Code == "ERROR_PATH_ENDS" && i.NodeId == toast.NodeId);
    }

    [Fact]
    public void A_telegram_step_without_a_chat_names_the_chat_field()
    {
        var node = new TelegramNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "Ping my phone",
            Ports = [],
            Config = new TelegramConfig { IntegrationRef = Guid.NewGuid(), Message = "hi" }
        };

        var issue = Assert.Single(Errors(node));

        Assert.Equal("TELEGRAM_CHAT_MISSING", issue.Code);
        Assert.Equal("config.chatId", issue.Field);
    }

    private static List<ValidationIssue> IssuesFor(BaseNode node)
    {
        var def = ValidWorkflowBuilder.Build();
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, node] });
        return result.Issues.Where(i => i.NodeId == node.NodeId).ToList();
    }

    private static List<ValidationIssue> Errors(BaseNode node) => IssuesFor(node).Where(i => i.Severity == ValidationSeverity.Error).ToList();

    private static SendClientMessageNode Send(string clientRef, string type) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "Heater on",
        Ports = [],
        Config = new SendClientMessageConfig { ClientRef = clientRef, Type = type }
    };

    private static WebhookNotificationNode Webhook(string url, string method) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "call",
        Ports = [],
        Config = new WebhookNotificationConfig { Url = url, Method = method }
    };

    private static VariableNode Variable(VariableScope scope, string name, JsonElement value) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "set",
        Ports = [],
        Config = new VariableConfig { Scope = scope, Op = VariableOperation.Set, Var = name, Value = value }
    };

    private static LogicGateNode Gate(WorkflowExpression condition) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "hot?",
        Ports = [],
        Config = new LogicGateConfig { Condition = condition }
    };

    private static DelayNode Delay(TimeSpan duration) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "wait",
        Ports = [],
        Config = new DelayConfig { Duration = duration }
    };

    private static ToastNotificationNode Toast(string title) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "toast",
        Ports = [],
        Config = new ToastConfig { Title = title, Message = "m" }
    };
}
