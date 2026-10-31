using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Validation;

/// <summary>
/// The checks on what is filled in on each step, as opposed to how the steps are wired. A setting
/// left blank arrives as an empty value rather than failing the whole request, so every problem in a
/// definition is listed at once, each with the field to fix.
/// </summary>
public sealed partial class WorkflowValidator
{
    /// <summary>The longest a Delay step may wait. Past this it reads as a mistake, not a schedule.</summary>
    public static readonly TimeSpan MaxDelayDuration = TimeSpan.FromDays(365);

    private static readonly JsonSerializerOptions ExpressionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static void ValidateStepSettings(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            void Error(string code, string message, string field) =>
                issues.Add(new ValidationIssue(ValidationSeverity.Error, code, $"Step '{node.Name}': {message}", node.NodeId, field));

            switch (node)
            {
                case SendClientMessageNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.ClientRef))
                    {
                        Error("CLIENT_REF_REQUIRED", "pick a device to send to.", "config.clientRef");
                    }
                    else if (!Guid.TryParse(config.ClientRef, out _))
                    {
                        Error("CLIENT_REF_INVALID", $"'{config.ClientRef}' is not a device id.", "config.clientRef");
                    }

                    if (string.IsNullOrWhiteSpace(config.Type))
                    {
                        Error("COMMAND_REQUIRED", "pick a command to send.", "config.type");
                    }

                    break;

                case EmailNotificationNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.To))
                    {
                        Error("EMAIL_RECIPIENT_MISSING", "add a recipient.", "config.to");
                    }

                    if (string.IsNullOrWhiteSpace(config.Subject))
                    {
                        Error("EMAIL_SUBJECT_MISSING", "add a subject.", "config.subject");
                    }

                    break;

                case ToastNotificationNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.Title))
                    {
                        Error("TOAST_TITLE_MISSING", "add a title.", "config.title");
                    }

                    if (string.IsNullOrWhiteSpace(config.Message))
                    {
                        Error("TOAST_MESSAGE_MISSING", "add a message.", "config.message");
                    }

                    break;

                case WebhookNotificationNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.Url))
                    {
                        Error("WEBHOOK_URL_MISSING", "add the URL to call.", "config.url");
                    }
                    else if (Uri.TryCreate(config.Url, UriKind.Absolute, out var uri) && !config.Url.StartsWith('/')
                        && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp && !TemplatePlaceholders.Contains(config.Url))
                    {
                        Error("WEBHOOK_URL_INVALID", $"'{config.Url}' must be an http or https URL.", "config.url");
                    }

                    if (!WebhookMethods.Contains(config.Method ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        Error("WEBHOOK_METHOD_INVALID", $"the method must be one of {string.Join(", ", WebhookMethods)}.", "config.method");
                    }

                    break;

                case VariableNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.Var))
                    {
                        Error("VARIABLE_NAME_MISSING", "name the variable to set.", "config.var");
                    }

                    ValidateLiteralOrExpression(node, config.Value, "config.value", issues);
                    ValidateLiteralOrExpression(node, config.Expected, "config.expected", issues);
                    break;

                case AwaitSignalNode { Config: { } config }:
                    if (string.IsNullOrWhiteSpace(config.SignalName))
                    {
                        Error("SIGNAL_NAME_MISSING", "name the signal to wait for.", "config.signalName");
                    }

                    if (config.Ttl is { } ttl && ttl <= TimeSpan.Zero)
                    {
                        Error("AWAIT_SIGNAL_TTL_INVALID", "the time to wait must be more than zero, or left out to wait without a limit.", "config.ttl");
                    }

                    break;

                case ForEachNode { Config: { } config } when string.IsNullOrWhiteSpace(config.Collection):
                    Error("FOREACH_COLLECTION_MISSING", "name the list to go through.", "config.collection");
                    break;

                case ParallelForEachNode { Config: { } config } when string.IsNullOrWhiteSpace(config.Collection):
                    Error("FOREACH_COLLECTION_MISSING", "name the list to go through.", "config.collection");
                    break;

                case SubWorkflowNode { Config: { } config } when config.WorkflowRefId == Guid.Empty:
                    Error("SUBWORKFLOW_TARGET_MISSING", "pick the workflow to start.", "config.workflowRefId");
                    break;

                case DelayNode { Config: { } config }:
                    if (config.Duration < TimeSpan.Zero || config.Duration > MaxDelayDuration)
                    {
                        Error("DELAY_DURATION_INVALID", $"the wait must be between zero and {MaxDelayDuration.TotalDays:0} days.", "config.duration");
                    }
                    else if (config.Duration == TimeSpan.Zero)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            "DELAY_DURATION_ZERO",
                            $"Step '{node.Name}': the wait is zero, so the step goes on at once.",
                            node.NodeId,
                            "config.duration"));
                    }

                    break;

                case WaitForHttpNode { Config: { } config } when config.Ttl <= TimeSpan.Zero:
                    Error("WAITFORHTTP_TTL_INVALID", "the time to wait for the call must be more than zero.", "config.ttl");
                    break;

                case ForkNode { Config: { } config }:
                    if (config.Branches is not { Count: > 0 } branches)
                    {
                        Error("FORK_BRANCHES_MISSING", "add at least one branch.", "config.branches");
                        break;
                    }

                    if (branches.Any(string.IsNullOrWhiteSpace))
                    {
                        Error("FORK_BRANCH_INVALID", "every branch needs a name.", "config.branches");
                    }

                    foreach (string duplicate in branches.Where(b => !string.IsNullOrWhiteSpace(b)).GroupBy(b => b, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
                    {
                        Error("FORK_BRANCH_DUPLICATE", $"branch '{duplicate}' is named twice.", "config.branches");
                    }

                    break;
            }
        }
    }

    private static readonly string[] WebhookMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>
    /// The expressions a run evaluates: a Logic step's condition, a client trigger's filter and a
    /// sub-workflow's inputs. Each must name something to read (a path, a variable) and hold
    /// templates that parse, or it fails every run that reaches it.
    /// </summary>
    private static void ValidateExpressions(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            switch (node)
            {
                case LogicGateNode { Config: { } config }:
                    if (config.Condition is null)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, "LOGIC_CONDITION_MISSING", $"Step '{node.Name}': add a condition.", node.NodeId, "config.condition"));
                    }
                    else
                    {
                        CheckExpression(node, config.Condition, "config.condition", issues);
                    }

                    break;

                case ClientTriggerNode { Config.Filter: { } filter }:
                    CheckExpression(node, filter, "config.filter", issues);
                    break;

                case SubWorkflowNode { Config.Input: { } input }:
                    foreach (var (key, expression) in input)
                    {
                        CheckExpression(node, expression, $"config.input.{key}", issues);
                    }

                    break;
            }
        }
    }

    // A Variable step's value is a plain JSON literal, or an expression tree marked by its "kind",
    // which the step only reads when it runs.
    private static void ValidateLiteralOrExpression(BaseNode node, JsonElement? value, string field, List<ValidationIssue> issues)
    {
        if (value is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String)
        {
            return;
        }

        WorkflowExpression? expression;
        try
        {
            expression = JsonSerializer.Deserialize<WorkflowExpression>(element.GetRawText(), ExpressionJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "EXPRESSION_INVALID", $"Step '{node.Name}', {field}: the expression cannot be read ({ex.Message}).", node.NodeId, field));
            return;
        }

        if (expression is not null)
        {
            CheckExpression(node, expression, field, issues);
        }
    }

    private static void CheckExpression(BaseNode node, WorkflowExpression expression, string field, List<ValidationIssue> issues)
    {
        void Error(string problem) =>
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "EXPRESSION_INVALID", $"Step '{node.Name}', {field}: {problem}", node.NodeId, field));

        foreach (var part in Walk(expression))
        {
            switch (part)
            {
                case null:
                    Error("an operand is missing.");
                    break;
                case BranchStateRefExpression { Path: var path } when string.IsNullOrWhiteSpace(path):
                    Error("a value reference names no path.");
                    break;
                case SharedVariableRefExpression { Name: var name } when string.IsNullOrWhiteSpace(name):
                    Error("a shared variable reference names no variable.");
                    break;
                case JsonPathExpression { Path: var path } when string.IsNullOrWhiteSpace(path):
                    Error("a JSON path is empty.");
                    break;
                case MemberAccessExpression member when string.IsNullOrWhiteSpace(member.Object) || string.IsNullOrWhiteSpace(member.Member):
                    Error("a member access names no object or member.");
                    break;
                case TemplateExpression { Template: var template } when !TemplatePlaceholders.TryParse(template ?? string.Empty, out _, out string? error):
                    Error(error);
                    break;
            }
        }
    }

    /// <summary>Every node of an expression tree, depth first; a missing operand comes back as null.</summary>
    private static IEnumerable<WorkflowExpression?> Walk(WorkflowExpression? expression)
    {
        yield return expression;
        IEnumerable<WorkflowExpression?> children = expression switch
        {
            BinaryExpression binary => [binary.Left, binary.Right],
            UnaryExpression unary => [unary.Operand],
            FunctionExpression function => function.Arguments ?? [],
            JsonPathExpression path => [path.BaseExpression],
            _ => []
        };

        foreach (var child in children)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// A <c>$shared.NAME</c> read of a variable this workflow never sets. It may have been set from the
    /// console, so this is a warning: more often the name is misspelt, and the read comes back empty.
    /// </summary>
    private static void WarnOnUndeclaredSharedVariables(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var declared = def.Nodes
            .OfType<VariableNode>()
            .Select(n => n.Config)
            .OfType<VariableConfig>()
            .Where(c => c.Scope == VariableScope.Shared && !string.IsNullOrWhiteSpace(c.Var))
            .Select(c => c.Var)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in def.Nodes)
        {
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, field) in SharedReads(node))
            {
                if (!declared.Contains(name) && reported.Add(name))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "SHARED_VARIABLE_UNDECLARED",
                        $"Step '{node.Name}' reads shared variable '{name}', which no step in this workflow sets.",
                        node.NodeId,
                        field));
                }
            }
        }
    }

    private static IEnumerable<(string Name, string Field)> SharedReads(BaseNode node)
    {
        if (node is ITemplatedStep step)
        {
            foreach (TemplateFields.Field field in TemplateFields.Find(step.TemplatedConfig))
            {
                foreach (string name in SharedNamesIn(field.Value))
                {
                    yield return (name, $"config.{field.Path}");
                }
            }
        }

        IEnumerable<(WorkflowExpression Expression, string Field)> expressions = node switch
        {
            LogicGateNode { Config.Condition: { } condition } => [(condition, "config.condition")],
            ClientTriggerNode { Config.Filter: { } filter } => [(filter, "config.filter")],
            SubWorkflowNode { Config.Input: { } input } => input.Select(i => (i.Value, $"config.input.{i.Key}")),
            _ => []
        };

        foreach (var (expression, field) in expressions)
        {
            foreach (var part in Walk(expression))
            {
                switch (part)
                {
                    case SharedVariableRefExpression { Name: { Length: > 0 } name }:
                        yield return (name, field);
                        break;
                    case TemplateExpression { Template: { } template }:
                        foreach (string name in SharedNamesIn(template))
                        {
                            yield return (name, field);
                        }

                        break;
                }
            }
        }
    }

    private static IEnumerable<string> SharedNamesIn(string? template)
    {
        if (!TemplatePlaceholders.Contains(template) || !TemplatePlaceholders.TryParse(template!, out var parts, out _))
        {
            return [];
        }

        return parts
            .Where(p => p.IsPlaceholder)
            .Select(p => TemplatePlaceholders.ToExpression(p.Text))
            .OfType<SharedVariableRefExpression>()
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n));
    }

    /// <summary>
    /// An action's error port that leads nowhere, or straight to an End: a failure taken that way is
    /// handled by doing nothing, and the run reads as if the step had been fine.
    /// </summary>
    private static void WarnOnDeadEndErrorPaths(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var nodes = def.Nodes.GroupBy(n => n.NodeId).ToDictionary(g => g.Key, g => g.First());
        var edges = def.Edges.GroupBy(e => (e.From.NodeId, e.From.PortId)).ToDictionary(g => g.Key, g => g.First());

        foreach (var node in def.Nodes.OfType<BaseActionNode>())
        {
            if (!node.Ports.Any(p => p.PortId == ErrorPortId && p.Direction == PortDirection.Output))
            {
                continue;
            }

            bool deadEnd = !edges.TryGetValue((node.NodeId, ErrorPortId), out var edge)
                || (nodes.TryGetValue(edge.To.NodeId, out var next) && next is EndNode);
            if (deadEnd)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "ERROR_PATH_ENDS",
                    $"Step '{node.Name}': nothing runs after this step's error port, so the error path ends here.",
                    node.NodeId));
            }
        }
    }
}
