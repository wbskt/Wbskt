using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Abstraction.Validation;

public sealed class WorkflowValidator
{
    // A WaitForHttp wake token is the sole secret gating an anonymous public callback, so it must be
    // long enough not to be brute-forceable. 24 characters ~= 128 bits when random (e.g. a GUID "N"
    // form or a base64 nonce).
    private const int MinWakeTokenLength = 24;
    private const string ErrorPortId = "error";
    private const string BodyPortId = "body";

    public ValidationResult Validate(WorkflowDefinition? definition)
    {
        var issues = new List<ValidationIssue>();
        if (definition is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "NULL_DEFINITION", "Workflow definition must not be null."));
            return new ValidationResult(issues);
        }
        ValidateShape(definition, issues);
        ValidateDuplicateNodeIds(definition, issues);
        ValidateEdges(definition, issues);
        ValidateDuplicatePortEdges(definition, issues);
        ValidatePorts(definition, issues);
        ValidateNodeKinds(definition, issues);
        ValidateNodeConfigs(definition, issues);
        ValidateOnFailure(definition, issues);
        ValidateParallelForEachPairing(definition, issues);
        ValidateSubWorkflowInputs(definition, issues);
        ValidateTriggerConfigs(definition, issues);
        WarnIfNoTriggers(definition, issues);
        WarnIfOrphans(definition, issues);
        return new ValidationResult(issues);
    }

    /// <summary>
    /// Two edges leaving the same port would make the next node ambiguous. The branch loop resolves
    /// the next node with SingleOrDefault, so this is not merely undefined - it throws at runtime,
    /// mid-run. Multi-edge fan-out is deliberately not supported; use a Fork node.
    /// </summary>
    private static void ValidateDuplicatePortEdges(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var duplicates = def.Edges
            .GroupBy(edge => (edge.From.NodeId, edge.From.PortId))
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "DUPLICATE_PORT_EDGE",
                $"Port '{group.Key.PortId}' on node '{group.Key.NodeId}' has {group.Count()} outbound edges; a port may have at most one. Use a Fork node to branch.",
                group.Key.NodeId));
        }
    }

    /// <summary>
    /// Rejects kinds the engine cannot run, so an author finds out at publish rather than watching a
    /// run fail partway through.
    /// </summary>
    private static void ValidateNodeKinds(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            if (!NodeKind.All.Contains(node.Kind))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "UNKNOWN_NODE_KIND",
                    $"Node '{node.NodeId}' has unknown kind '{node.Kind}'.",
                    node.NodeId));
            }
            else if (NodeKind.NotYetImplemented.Contains(node.Kind))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "NODE_KIND_NOT_IMPLEMENTED",
                    $"Node '{node.NodeId}' uses kind '{node.Kind}', which is not implemented yet and would fail at runtime.",
                    node.NodeId));
            }
        }
    }

    private static void ValidateOnFailure(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var nodeIds = def.Nodes.Select(node => node.NodeId).ToHashSet();

        foreach (var node in def.Nodes)
        {
            if (node is not Models.Nodes.Actions.BaseActionNode { OnFailure: { } onFailure })
            {
                continue;
            }

            // ContinueOnError leaves through an "error" port; without one the outcome silently does
            // nothing and the branch behaves as if the failure had been ignored.
            if (onFailure.Outcome == ErrorOutcome.ContinueOnError
                && !node.Ports.Any(port => port.PortId == ErrorPortId && port.Direction == PortDirection.Output))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT",
                    $"Node '{node.NodeId}' declares onFailure 'ContinueOnError' but has no output port '{ErrorPortId}' to leave through.",
                    node.NodeId));
            }

            if (onFailure.TargetNodeId is Guid target && !nodeIds.Contains(target))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "ONFAILURE_TARGET_NOT_FOUND",
                    $"Node '{node.NodeId}' has onFailure targetNodeId '{target}', which is not a node in this workflow.",
                    node.NodeId));
            }
        }
    }

    /// <summary>
    /// The engine writes <c>parentRunRefId</c> into the child's body so the completion hook can find its
    /// way back to the parent. An input of the same name would overwrite it and strand the parent on its
    /// bookmark, so it is rejected here rather than at runtime.
    /// </summary>
    private static void ValidateSubWorkflowInputs(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes.OfType<SubWorkflowNode>())
        {
            if (node.Config?.Input is not { Count: > 0 } input)
            {
                continue;
            }

            foreach (string key in input.Keys.Where(SubWorkflowConfig.ReservedInputKeys.Contains))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "SUBWORKFLOW_INPUT_KEY_RESERVED",
                    $"SubWorkflow node '{node.NodeId}' supplies input '{key}', which the engine writes itself; rename it.",
                    node.NodeId));
            }
        }
    }

    /// <summary>
    /// A ParallelForEach cohort converges on a Join; without one the aggregator is never satisfied,
    /// so everything downstream is silently skipped and the run hangs until the reaper collects it.
    /// A Fork without a Join is legal - its branches simply run to their own ends.
    /// </summary>
    private static void ValidateParallelForEachPairing(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes.OfType<ParallelForEachNode>())
        {
            if (WorkflowGraph.FindDownstream<JoinNode>(def, node.NodeId, BodyPortId) is null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "PARALLEL_FOREACH_WITHOUT_JOIN",
                    $"ParallelForEach node '{node.NodeId}' has no Join node downstream of its '{BodyPortId}' port; the cohort could never converge.",
                    node.NodeId));
            }
        }
    }

    private static void ValidateTriggerConfigs(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            switch (node)
            {
                case ScheduleTriggerNode schedule:
                    // An invalid cron currently throws *after* the definition row is inserted,
                    // leaving a published workflow that can never fire.
                    if (!CronParser.TryParse(schedule.Config?.Cron ?? string.Empty, out _))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            "INVALID_CRON",
                            $"Schedule trigger '{node.NodeId}' has an invalid cron expression '{schedule.Config?.Cron}'.",
                            node.NodeId));
                    }
                    WarnOnCorrelation(node.NodeId, schedule.Config?.CorrelationKey, null, issues);
                    break;

                case ClientTriggerNode client:
                    WarnOnCorrelation(node.NodeId, client.Config?.CorrelationKey, client.Config?.ConcurrencyPolicy, issues);
                    break;

                case ClientPresenceTriggerNode presence:
                    // The key is built from the parsed GUID, and the socket host reports presence by
                    // GUID, so anything else would publish a trigger that can never fire.
                    if (!Guid.TryParse(presence.Config?.ClientRef, out _))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            "INVALID_CLIENT_REF",
                            $"Presence trigger '{node.NodeId}' needs a client id, but has '{presence.Config?.ClientRef}'.",
                            node.NodeId));
                    }
                    if (presence.Config is { } config
                        && (config.ForSeconds < 0 || config.ForSeconds > ClientPresenceTriggerConfig.MaxForSeconds))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            "INVALID_PRESENCE_DURATION",
                            $"Presence trigger '{node.NodeId}' has forSeconds {config.ForSeconds}; it must be between 0 and {ClientPresenceTriggerConfig.MaxForSeconds}.",
                            node.NodeId));
                    }
                    WarnOnCorrelation(node.NodeId, presence.Config?.CorrelationKey, presence.Config?.ConcurrencyPolicy, issues);
                    break;

                case WebhookTriggerNode webhook:
                    WarnOnCorrelation(node.NodeId, webhook.Config?.CorrelationKey, webhook.Config?.ConcurrencyPolicy, issues);
                    break;
            }
        }
    }

    private static void WarnOnCorrelation(
        Guid nodeId,
        string? correlationKey,
        WorkflowConcurrencyPolicy? concurrencyPolicy,
        List<ValidationIssue> issues)
    {
        if (correlationKey is not null
            && !correlationKey.StartsWith("$trigger.", StringComparison.OrdinalIgnoreCase)
            && correlationKey.Contains('$'))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SUSPICIOUS_CORRELATION_KEY",
                $"Trigger '{nodeId}' has correlationKey '{correlationKey}', which is neither a '$trigger.' path nor a plain constant; it will be used verbatim.",
                nodeId));
        }

        // Every concurrency policy other than AllowParallel groups runs by correlation key. With no
        // key there is nothing to group by, so the policy silently degrades.
        if (concurrencyPolicy is not null
            && concurrencyPolicy != WorkflowConcurrencyPolicy.AllowParallel
            && string.IsNullOrWhiteSpace(correlationKey))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "CONCURRENCY_POLICY_WITHOUT_CORRELATION_KEY",
                $"Trigger '{nodeId}' sets concurrencyPolicy '{concurrencyPolicy}' but has no correlationKey, so it will behave as AllowParallel.",
                nodeId));
        }
    }

    private static void ValidateNodeConfigs(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            if (node is WaitForHttpNode waitForHttp)
            {
                string? token = waitForHttp.Config?.Token?.Trim();
                if (string.IsNullOrEmpty(token) || token.Length < MinWakeTokenLength)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "WAITFORHTTP_WEAK_TOKEN",
                        $"WaitForHttp node '{node.NodeId}' must define a 'token' of at least {MinWakeTokenLength} characters; it is the only secret gating the public wake callback.",
                        node.NodeId));
                }
            }
        }
    }

    private static void ValidateShape(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        if (def.WorkflowRefId == Guid.Empty)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "INVALID_WORKFLOW_REF_ID", "WorkflowRefId must not be empty."));
        }

        if (def.Version < 1)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "INVALID_VERSION", "Version must be >= 1."));
        }
    }

    private static void ValidateDuplicateNodeIds(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var duplicateNodeIds = def.Nodes
            .GroupBy(n => n.NodeId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var nodeId in duplicateNodeIds)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "DUPLICATE_NODE_ID", $"Multiple nodes have the same NodeId '{nodeId}'.", nodeId));
        }
    }

    private static void ValidateEdges(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var nodeMap = def.Nodes
            .GroupBy(n => n.NodeId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var edge in def.Edges)
        {
            if (!nodeMap.TryGetValue(edge.From.NodeId, out var fromNode))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_NODE", $"Edge references unknown source node '{edge.From.NodeId}'."));
                continue;
            }

            if (!nodeMap.TryGetValue(edge.To.NodeId, out var toNode))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_NODE", $"Edge references unknown target node '{edge.To.NodeId}'."));
                continue;
            }

            var fromPortExists = fromNode.Ports.Any(p => p.PortId == edge.From.PortId);
            if (!fromPortExists)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_PORT", $"Edge references unknown port '{edge.From.PortId}' on node '{edge.From.NodeId}'.", edge.From.NodeId));
            }

            var toPortExists = toNode.Ports.Any(p => p.PortId == edge.To.PortId);
            if (!toPortExists)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_PORT", $"Edge references unknown port '{edge.To.PortId}' on node '{edge.To.NodeId}'.", edge.To.NodeId));
            }
        }
    }

    private static void WarnIfNoTriggers(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var hasTrigger = def.Nodes.Any(n => n is ClientTriggerNode or ClientPresenceTriggerNode or ScheduleTriggerNode or WebhookTriggerNode or ManualTriggerNode);

        if (!hasTrigger)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NO_TRIGGERS", "Workflow has no trigger nodes; it can never start a run."));
        }
    }

    private static void WarnIfOrphans(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var connectedNodeIds = new HashSet<Guid>();

        foreach (var edge in def.Edges)
        {
            connectedNodeIds.Add(edge.From.NodeId);
            connectedNodeIds.Add(edge.To.NodeId);
        }

        foreach (var node in def.Nodes)
        {
            if (!connectedNodeIds.Contains(node.NodeId))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "ORPHAN_NODE", $"Node '{node.NodeId}' ('{node.Name}') has no inbound or outbound edges.", node.NodeId));
            }
        }
    }

    private static void ValidatePorts(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            var expectedPorts = GetExpectedPorts(node);
            
            // Check for missing ports
            foreach (var expected in expectedPorts)
            {
                if (!node.Ports.Any(p => p.PortId == expected.PortId && p.Direction == expected.Direction))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MISSING_PORT", $"Node '{node.NodeId}' of kind '{node.Kind}' is missing expected {expected.Direction} port '{expected.PortId}'."));
                }
            }

            // Check for extra ports. Any action node MAY declare an optional "error" output - it is
            // what an OnFailure of ContinueOnError leaves through - so it is never "unexpected".
            bool allowsOptionalErrorPort = node.Kind.StartsWith("action:", StringComparison.Ordinal);

            foreach (var actual in node.Ports)
            {
                if (expectedPorts.Any(p => p.PortId == actual.PortId && p.Direction == actual.Direction))
                {
                    continue;
                }

                if (allowsOptionalErrorPort && actual.PortId == ErrorPortId && actual.Direction == PortDirection.Output)
                {
                    continue;
                }

                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EXTRA_PORT", $"Node '{node.NodeId}' of kind '{node.Kind}' defines an unexpected {actual.Direction} port '{actual.PortId}'."));
            }
        }
    }

    private static IReadOnlyCollection<PortDefinition> GetExpectedPorts(Models.Nodes.BaseNode node)
    {
        var inputIn = new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" };
        var outputDefault = new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" };
        var outputTrue = new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" };
        var outputFalse = new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" };
        var outputBody = new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" };
        var outputDone = new PortDefinition { PortId = "done", Direction = PortDirection.Output, Label = "Done" };
        var outputEmpty = new PortDefinition { PortId = "empty", Direction = PortDirection.Output, Label = "Empty" };

        if (node.Kind.StartsWith("trigger:"))
        {
            return [outputDefault];
        }
        
        if (node.Kind.StartsWith("action:"))
        {
            if (node.Kind == "action:webhook")
            {
                return [inputIn, outputDefault, new PortDefinition { PortId = "error", Direction = PortDirection.Output, Label = "Error" }
                ];
            }
            return [inputIn, outputDefault];
        }

        switch (node.Kind)
        {
            case Models.Nodes.NodeKind.ControlLogic:
                return [inputIn, outputTrue, outputFalse];
            
            case Models.Nodes.NodeKind.ControlForEach:
                return [inputIn, outputBody, outputDone];

            case Models.Nodes.NodeKind.ControlParallelForEach:
                return [inputIn, outputBody, outputEmpty];
            
            case Models.Nodes.NodeKind.ControlFork:
                var forkOutputs = new List<PortDefinition> { inputIn };
                if (node is Models.Nodes.Controls.ForkNode { Config: not null } forkNode)
                {
                    foreach (var branch in forkNode.Config.Branches)
                    {
                        forkOutputs.Add(new PortDefinition { PortId = branch, Direction = PortDirection.Output, Label = branch });
                    }
                }
                return forkOutputs;
                
            case Models.Nodes.NodeKind.ControlAwaitSignal:
                if (node is Models.Nodes.Controls.AwaitSignalNode awaitNode && awaitNode.Config?.Ttl is not null)
                {
                    return [inputIn, outputDefault, new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }];
                }
                return [inputIn, outputDefault];

            case Models.Nodes.NodeKind.ControlWaitForHttp:
                // WaitForHttp always has a mandatory Ttl, so the timeout port is always present.
                return [inputIn, outputDefault, new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }];
                
            case Models.Nodes.NodeKind.ControlEnd:
            case Models.Nodes.NodeKind.ControlFailRun:
                return [inputIn];

            default:
                // Delay, Join, Variable, SubWorkflow
                return [inputIn, outputDefault];
        }
    }
}


