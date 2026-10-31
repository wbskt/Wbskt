using Wbskt.Protocol;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
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

    /// <param name="definition">The definition to check.</param>
    /// <param name="workflowRefId">
    /// The id the definition is being published under, when the caller knows it. The definition names
    /// itself too (<see cref="WorkflowDefinition.WorkflowRefId"/>), but publishing stores it under the
    /// request's id without requiring the two to agree, so a self-referencing sub-workflow step is
    /// checked against both.
    /// </param>
    public ValidationResult Validate(WorkflowDefinition? definition, Guid? workflowRefId = null)
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
        ValidateIntegrationSteps(definition, issues);
        ValidateOnFailure(definition, issues);
        ValidateRetryPolicies(definition, issues);
        ValidateParallelForEachPairing(definition, issues);
        ValidateForkJoinPairing(definition, issues);
        ValidateJoinsBelongToCohorts(definition, issues);
        ValidateSubWorkflowInputs(definition, issues);
        ValidateSubWorkflowSelfReference(definition, workflowRefId, issues);
        ValidateEndOutputs(definition, issues);
        ValidateLocalVariableNames(definition, issues);
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
    /// Rejects a kind with no descriptor in <see cref="NodeKinds.All"/>. Such a node has no expected
    /// ports, no price and no executor, so it would publish with nothing checked and fail mid-run.
    /// </summary>
    private static void ValidateNodeKinds(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            if (!NodeKinds.TryGet(node.Kind, out _))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "UNKNOWN_NODE_KIND",
                    $"Node '{node.NodeId}' has unknown kind '{node.Kind}'.",
                    node.NodeId));
            }
        }
    }

    private static void ValidateOnFailure(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var nodeIds = def.Nodes.Select(node => node.NodeId).ToHashSet();

        // Compensation is not part of this release. Both switches are still read from a saved
        // definition, so an author who sets one must hear at publish that it does nothing, rather
        // than find out from a failed run that no undo step ran.
        if (def.RunCompensationOnFailure)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "COMPENSATION_NOT_SUPPORTED",
                "The workflow sets runCompensationOnFailure, but compensation is not available in this release. Remove the setting; a failure then fails the branch (or the run, with failFast)."));
        }

        foreach (var node in def.Nodes)
        {
            if (node is not Models.Nodes.Actions.BaseActionNode { OnFailure: { } onFailure })
            {
                continue;
            }

            if (onFailure.Outcome == ErrorOutcome.Compensate)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "COMPENSATION_NOT_SUPPORTED",
                    $"Node '{node.NodeId}' declares onFailure 'Compensate', but compensation is not available in this release. Use FailBranch or FailRun instead.",
                    node.NodeId));
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
    /// The factor and the ceiling were accepted but ignored until the retry executor started reading
    /// them; values that were harmless while ignored now shape the backoff. A factor of zero or less
    /// makes every retry after the first immediate (or the delay meaningless), and a ceiling below the
    /// initial delay silently overrides it on every attempt, so both are rejected as authoring errors.
    /// </summary>
    private static void ValidateRetryPolicies(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            if (node is not BaseActionNode { Retry: { } retry })
            {
                continue;
            }

            if (retry.Factor is { } factor && (!double.IsFinite(factor) || factor <= 0))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "RETRY_FACTOR_INVALID",
                    $"Node '{node.NodeId}': the retry factor must be a number greater than zero.",
                    node.NodeId));
            }

            if (retry.MaxDelay is { } maxDelay && maxDelay < retry.InitialDelay)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "RETRY_MAX_DELAY_BELOW_INITIAL",
                    $"Node '{node.NodeId}': the retry maxDelay must not be less than the initialDelay.",
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
    /// An End node's output is only ever read by a parent's sub-workflow step, and a parent starts its
    /// child through the child's manual trigger. On a workflow with none, the output could never reach
    /// anyone, so it is rejected rather than evaluated on every run for nothing.
    /// </summary>
    private static void ValidateEndOutputs(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        if (def.Nodes.Any(n => n is ManualTriggerNode))
        {
            return;
        }

        foreach (var node in def.Nodes.OfType<EndNode>().Where(n => n.Config?.Output is not null))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "END_OUTPUT_WITHOUT_MANUAL_TRIGGER",
                $"End node '{node.NodeId}' declares an output, but the workflow has no manual trigger, so no parent can start it as a sub-workflow to read it; add a manual trigger or remove the output.",
                node.NodeId));
        }
    }

    /// <summary>
    /// A sub-workflow step that starts its own workflow starts a child that reaches the same step and
    /// starts another, and a child is exempt from the workspace run ceiling. The engine's depth ceiling
    /// stops it eventually; this stops the direct case before it is ever published. A cycle through
    /// another workflow cannot be seen from one definition and is left to the depth ceiling.
    /// </summary>
    private static void ValidateSubWorkflowSelfReference(WorkflowDefinition def, Guid? workflowRefId, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes.OfType<SubWorkflowNode>())
        {
            if (node.Config is not { } config || config.WorkflowRefId == Guid.Empty)
            {
                continue;
            }

            if (config.WorkflowRefId == def.WorkflowRefId || config.WorkflowRefId == workflowRefId)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "SUBWORKFLOW_SELF_REFERENCE",
                    $"SubWorkflow node '{node.NodeId}' starts its own workflow, which would start children without end; point it at another workflow.",
                    node.NodeId));
            }
        }
    }

    /// <summary>
    /// A Local variable is written straight into the branch's local state, which is also where the
    /// engine keeps the trigger payload and its own bookkeeping. One named after an engine key would
    /// overwrite it - a variable called <c>__join_token</c> strands its cohort at the Join - so the
    /// engine's names are refused here.
    /// </summary>
    private static void ValidateLocalVariableNames(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes.OfType<VariableNode>())
        {
            if (node.Config is not { Scope: VariableScope.Local } config || string.IsNullOrEmpty(config.Var))
            {
                continue;
            }

            if (BranchStateKeys.IsReserved(config.Var))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "VARIABLE_NAME_RESERVED",
                    $"Variable node '{node.NodeId}' sets local variable '{config.Var}', a name the engine keeps for itself (names starting with '{BranchStateKeys.EnginePrefix}', and '{BranchStateKeys.Trigger}'); rename it.",
                    node.NodeId));
            }
        }
    }

    /// <summary>
    /// A ParallelForEach cohort converges on a Join; without one the aggregator is never satisfied,
    /// so everything downstream is silently skipped and the run hangs until the reaper collects it.
    /// The Join must be the cohort's own, not one belonging to a Fork nested in the body. A Fork has
    /// its own rule: <see cref="ValidateForkJoinPairing"/>.
    /// </summary>
    private static void ValidateParallelForEachPairing(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes.OfType<ParallelForEachNode>())
        {
            if (WorkflowGraph.FindJoinForCohort(def, node.NodeId, [BodyPortId]) is null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "PARALLEL_FOREACH_WITHOUT_JOIN",
                    $"ParallelForEach node '{node.NodeId}' has no Join node downstream of its '{BodyPortId}' port; the cohort could never converge.",
                    node.NodeId));
            }
        }
    }

    /// <summary>
    /// A Fork whose branches reach a Join must have every branch reach that same Join before it can
    /// end. A Fork's cohort only advances when a member arrives at the Join, so a branch that ended
    /// first - at an End node, or through a port with no edge - never counted: a Mode=All join never
    /// converged, the run finalized Succeeded, and everything after the Join was skipped without a
    /// trace. Branches that converge on different Joins split one cohort between two aggregators, and
    /// neither ever fills.
    ///
    /// A Fork with no Join at its own level is a fire-and-forget fan-out and stays legal: its branches
    /// run to their own ends and the run ends when they all do. A FailRun node is an acceptable end
    /// too: it fails the whole run, so the join never needs to converge. The paths are walked by
    /// <see cref="WorkflowGraph.WalkCohort"/>, the same walk the engine uses to find a cohort's Join,
    /// with each node's expected ports so a port with no edge is seen. An "error" port with no edge is
    /// skipped: it is only taken with ContinueOnError, and that already requires the edge.
    /// </summary>
    private static void ValidateForkJoinPairing(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var portsWithEdges = def.Edges.Select(edge => (edge.From.NodeId, edge.From.PortId)).ToHashSet();
        IEnumerable<string> OutputPorts(BaseNode node) => GetExpectedPorts(node)
            .Where(port => port.Direction == PortDirection.Output)
            .Select(port => port.PortId)
            .Where(portId => portId != ErrorPortId || portsWithEdges.Contains((node.NodeId, portId)));

        foreach (ForkNode fork in def.Nodes.OfType<ForkNode>())
        {
            if (fork.Config is null)
            {
                continue;
            }

            CohortWalk walk = WorkflowGraph.WalkCohort(def, fork.NodeId, fork.Config.Branches, OutputPorts);
            if (walk.Joins.Count == 0)
            {
                continue;
            }

            if (walk.EndsBeforeJoin)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "FORK_BRANCH_WITHOUT_JOIN",
                    $"Fork node '{fork.NodeId}' has a branch that can end (at an End node, or a port with no edge) before reaching its Join; every branch must reach the Fork's Join or the join can never converge.",
                    fork.NodeId));
            }

            if (walk.Joins.Count > 1)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "FORK_BRANCHES_JOIN_MISMATCH",
                    $"Fork node '{fork.NodeId}' has branches that reach different Join nodes ({string.Join(", ", walk.Joins.Select(join => $"'{join.NodeId}'"))}); every branch of a Fork must converge on the same Join.",
                    fork.NodeId));
            }
        }
    }

    /// <summary>
    /// Every Join must be the Join some Fork or ParallelForEach converges on at its own level. A Join
    /// that is not - one placed after another Join, or on a path no fan-out feeds - is reached by a
    /// branch carrying no join token, and the engine fails that branch with JOIN_NO_TOKEN. Until the
    /// winner of a Join dropped its token, the second Join silently ended the branch instead, and
    /// everything after it was skipped, so this shape could be published and look like it ran.
    /// </summary>
    private static void ValidateJoinsBelongToCohorts(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var cohortJoins = new HashSet<Guid>();
        foreach (var node in def.Nodes)
        {
            IEnumerable<string>? ports = node switch
            {
                ForkNode { Config: not null } fork => fork.Config.Branches,
                ParallelForEachNode => [BodyPortId],
                _ => null
            };
            if (ports is null)
            {
                continue;
            }

            // Every Join reached at the cohort's level counts here; a Fork whose branches reach
            // different Joins is reported by its own rule, not twice.
            foreach (JoinNode join in WorkflowGraph.WalkCohort(def, node.NodeId, ports).Joins)
            {
                cohortJoins.Add(join.NodeId);
            }
        }

        foreach (var join in def.Nodes.OfType<JoinNode>().Where(join => !cohortJoins.Contains(join.NodeId)))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "JOIN_WITHOUT_COHORT",
                $"Join node '{join.NodeId}' is not the Join of any Fork or ParallelForEach at its level, so no branch that reaches it carries a cohort to join; remove it, or place it where a fan-out's branches converge.",
                join.NodeId));
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
                    WarnOnCorrelation(node.NodeId, schedule.Config?.CorrelationKey, issues);
                    break;

                case ClientTriggerNode client:
                    if (client.Config is { } clientConfig)
                    {
                        ValidateClientHold(node.NodeId, clientConfig, issues);
                    }
                    WarnOnCorrelation(node.NodeId, client.Config?.CorrelationKey, issues);
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
                    WarnOnCorrelation(node.NodeId, presence.Config?.CorrelationKey, issues);
                    break;

                case WebhookTriggerNode webhook:
                    WarnOnCorrelation(node.NodeId, webhook.Config?.CorrelationKey, issues);
                    break;
            }
        }
    }

    private static void ValidateClientHold(Guid nodeId, ClientTriggerConfig config, List<ValidationIssue> issues)
    {
        if (config.HoldSeconds < 0 || config.HoldSeconds > ClientTriggerConfig.MaxHoldSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "INVALID_HOLD_DURATION",
                $"Client trigger '{nodeId}' has holdSeconds {config.HoldSeconds}; it must be between 0 and {ClientTriggerConfig.MaxHoldSeconds}.",
                nodeId));
            return;
        }

        if (config.HoldSeconds == 0)
        {
            return;
        }

        // Holding means "the filter kept matching"; with no filter there is nothing to hold.
        if (config.Filter is null)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "HOLD_REQUIRES_FILTER",
                $"Client trigger '{nodeId}' has holdSeconds {config.HoldSeconds} but no filter; a hold time needs a filter to hold.",
                nodeId));
        }

        // An any-type hold is recorded against every message the device sends, and a message of another
        // type (its own state.report, say) fails the filter and clears the hold, so it would rarely fire.
        if (config.Type == "*")
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "HOLD_REQUIRES_MESSAGE_TYPE",
                $"Client trigger '{nodeId}' has holdSeconds {config.HoldSeconds} on any message type; a hold time needs one message type, because every other type would reset it.",
                nodeId));
        }

        // The hold key is built from the parsed GUID, as presence keys are.
        if (!Guid.TryParse(config.ClientRef, out _))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "INVALID_CLIENT_REF",
                $"Client trigger '{nodeId}' has a hold time and needs a client id, but has '{config.ClientRef}'.",
                nodeId));
        }
    }

    private static void WarnOnCorrelation(
        Guid nodeId,
        string? correlationKey,
        List<ValidationIssue> issues)
    {
        if (CorrelationKeyExpression.IsUnresolvableTemplate(correlationKey))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "INVALID_CORRELATION_KEY",
                $"Trigger '{nodeId}' has correlationKey '{correlationKey}'. A template must be the whole key and name a trigger field, e.g. '{{{{trigger.payload.room}}}}'; as written every run would share one key.",
                nodeId));
        }
        else if (correlationKey is not null
            && !CorrelationKeyExpression.TryGetTriggerPath(correlationKey, out _)
            && correlationKey.Contains('$'))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SUSPICIOUS_CORRELATION_KEY",
                $"Trigger '{nodeId}' has correlationKey '{correlationKey}', which is neither a '$trigger.' path nor a plain constant; it will be used verbatim.",
                nodeId));
        }
    }

    private static void ValidateNodeConfigs(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            // The same rules the console applies, so a workflow cannot send a device a sys.* frame.
            if (node is SendClientMessageNode sendMessage
                && DeviceMessageRules.FindViolation(sendMessage.Config?.Type, sendMessage.Config?.Payload?.GetRawText()) is { } violation)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CLIENT_MESSAGE_INVALID",
                    $"ClientMessage node '{node.NodeId}': {violation}",
                    node.NodeId));
            }

            if (node is SendClientMessageNode { Config.AckTimeout: { } ackTimeout }
                && (ackTimeout <= TimeSpan.Zero || ackTimeout > SendClientMessageConfig.MaxAckTimeout))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CLIENT_MESSAGE_ACK_TIMEOUT",
                    $"ClientMessage node '{node.NodeId}': the ack timeout must be more than zero and at most {SendClientMessageConfig.MaxAckTimeout.TotalHours:0} hours.",
                    node.NodeId));
            }

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

    /// <summary>
    /// Request headers a step may not set: ones the HTTP stack owns, and credentials, which belong in
    /// an integration (a header written into a definition is readable by the whole workspace and
    /// frozen into every published version).
    /// </summary>
    private static readonly HashSet<string> ForbiddenWebhookHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "TE", "Trailer",
        "Proxy-Connection", "Expect", "Authorization", "Proxy-Authorization", "Cookie"
    };

    /// <summary>
    /// What can be checked about integration steps from the definition alone: a step that needs an
    /// integration names one, and Call a URL's headers and relative URL make sense. Whether the named
    /// integration exists, and is of the right type, is the workspace's to answer, at publish.
    /// </summary>
    private static void ValidateIntegrationSteps(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            if (node is IIntegrationStep step && IntegrationTypes.IsRequiredFor(node.Kind) && step.IntegrationRef is null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "INTEGRATION_REF_MISSING",
                    $"Step '{node.Name}' must name the {IntegrationTypes.NameOf(IntegrationTypes.ForStepKind(node.Kind)!.Value)} integration it sends through (integrationRef).",
                    node.NodeId));
            }

            switch (node)
            {
                case WebhookNotificationNode webhook:
                    ValidateWebhookStep(webhook, issues);
                    break;
                case TelegramNotificationNode telegram:
                    ValidateTelegramStep(telegram, issues);
                    break;
                case DiscordNotificationNode discord:
                    ValidateDiscordStep(discord, issues);
                    break;
                case NtfyNotificationNode ntfy:
                    if (string.IsNullOrWhiteSpace(ntfy.Config?.Message))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, "NTFY_MESSAGE_MISSING", $"Step '{node.Name}' has no message.", node.NodeId));
                    }

                    if (ntfy.Config?.Priority is < 1 or > 5)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, "NTFY_PRIORITY_INVALID", $"Step '{node.Name}': priority must be 1 to 5.", node.NodeId));
                    }

                    break;
            }
        }
    }

    // Telegram's limit on a message's text. It counts the text after markup is parsed, so for a
    // formatted message this is a little stricter than Telegram itself.
    private const int TelegramMaxMessageLength = 4096;

    private static readonly string[] TelegramParseModes = ["MarkdownV2", "HTML"];

    private static void ValidateTelegramStep(TelegramNotificationNode node, List<ValidationIssue> issues)
    {
        var config = node.Config;
        void Add(string code, string message) =>
            issues.Add(new ValidationIssue(ValidationSeverity.Error, code, $"Step '{node.Name}': {message}", node.NodeId));

        if (string.IsNullOrWhiteSpace(config?.ChatId))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "TELEGRAM_CHAT_MISSING", $"Step '{node.Name}' has no chat to send to (chatId).", node.NodeId));
        }

        if (config is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.Message) || config.Message.Length > TelegramMaxMessageLength)
        {
            Add("TELEGRAM_MESSAGE_INVALID", $"the message must be 1-{TelegramMaxMessageLength} characters.");
        }

        if (config.ParseMode is not null && !TelegramParseModes.Contains(config.ParseMode, StringComparer.Ordinal))
        {
            Add("TELEGRAM_PARSE_MODE_INVALID", $"parseMode must be {string.Join(" or ", TelegramParseModes)}, or left out for plain text.");
        }

        if (config.MessageThreadId is not null && !IsTelegramId(config.MessageThreadId))
        {
            Add("TELEGRAM_THREAD_INVALID", "messageThreadId must be a topic id (digits only).");
        }
    }

    // Telegram's message and topic ids are positive 32-bit integers.
    private static bool IsTelegramId(string value) => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) && id > 0;

    // Discord's own limits; a message over any of them is refused whole.
    private const int DiscordMaxMessageLength = 2000;
    private const int DiscordMaxEmbeds = 10;
    private const int DiscordMaxEmbedTitle = 256;
    private const int DiscordMaxEmbedDescription = 4096;
    private const int DiscordMaxEmbedFields = 25;
    private const int DiscordMaxFieldName = 256;
    private const int DiscordMaxFieldValue = 1024;
    private const int DiscordMaxEmbedFooter = 2048;
    private const int DiscordMaxEmbedTotal = 6000;

    private static void ValidateDiscordStep(DiscordNotificationNode node, List<ValidationIssue> issues)
    {
        var config = node.Config;
        if (config is null)
        {
            return;
        }

        void Add(string code, string message) =>
            issues.Add(new ValidationIssue(ValidationSeverity.Error, code, $"Step '{node.Name}': {message}", node.NodeId));

        int embedCount = config.Embeds?.Count ?? 0;
        if (string.IsNullOrWhiteSpace(config.Message) && embedCount == 0)
        {
            Add("DISCORD_MESSAGE_INVALID", "needs a message or at least one embed.");
        }
        else if (config.Message?.Length > DiscordMaxMessageLength)
        {
            Add("DISCORD_MESSAGE_INVALID", $"the message must be at most {DiscordMaxMessageLength} characters.");
        }

        if (config.AvatarUrl is not null && !IsHttpUrl(config.AvatarUrl))
        {
            Add("DISCORD_AVATAR_URL_INVALID", "avatarUrl must be an absolute http or https URL.");
        }

        if (config.ThreadId is not null && !IsSnowflake(config.ThreadId))
        {
            Add("DISCORD_THREAD_INVALID", "threadId must be a Discord id (digits only).");
        }

        if (embedCount > DiscordMaxEmbeds)
        {
            Add("DISCORD_EMBED_INVALID", $"a message can carry at most {DiscordMaxEmbeds} embeds.");
        }

        int total = 0;
        for (int i = 0; i < embedCount; i++)
        {
            var embed = config.Embeds![i];
            string at = $"embed {i + 1}";
            int fieldCount = embed.Fields?.Count ?? 0;

            if (string.IsNullOrWhiteSpace(embed.Title) && string.IsNullOrWhiteSpace(embed.Description) && fieldCount == 0
                && string.IsNullOrWhiteSpace(embed.Footer) && embed.ImageUrl is null && embed.ThumbnailUrl is null)
            {
                Add("DISCORD_EMBED_INVALID", $"{at} has nothing to show; give it a title, description, field, footer or image.");
            }

            if (embed.Title?.Length > DiscordMaxEmbedTitle)
            {
                Add("DISCORD_EMBED_INVALID", $"{at}: the title must be at most {DiscordMaxEmbedTitle} characters.");
            }

            if (embed.Description?.Length > DiscordMaxEmbedDescription)
            {
                Add("DISCORD_EMBED_INVALID", $"{at}: the description must be at most {DiscordMaxEmbedDescription} characters.");
            }

            if (embed.Footer?.Length > DiscordMaxEmbedFooter)
            {
                Add("DISCORD_EMBED_INVALID", $"{at}: the footer must be at most {DiscordMaxEmbedFooter} characters.");
            }

            if (embed.Color is not null && !IsHexColor(embed.Color))
            {
                Add("DISCORD_EMBED_INVALID", $"{at}: color must be #RRGGBB.");
            }

            foreach (var (name, url) in new[] { ("url", embed.Url), ("imageUrl", embed.ImageUrl), ("thumbnailUrl", embed.ThumbnailUrl) })
            {
                if (url is not null && !IsHttpUrl(url))
                {
                    Add("DISCORD_EMBED_INVALID", $"{at}: {name} must be an absolute http or https URL.");
                }
            }

            if (fieldCount > DiscordMaxEmbedFields)
            {
                Add("DISCORD_EMBED_INVALID", $"{at} can have at most {DiscordMaxEmbedFields} fields.");
            }

            for (int f = 0; f < fieldCount; f++)
            {
                var field = embed.Fields![f];
                if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > DiscordMaxFieldName)
                {
                    Add("DISCORD_EMBED_INVALID", $"{at}, field {f + 1}: the name must be 1-{DiscordMaxFieldName} characters.");
                }

                if (string.IsNullOrWhiteSpace(field.Value) || field.Value.Length > DiscordMaxFieldValue)
                {
                    Add("DISCORD_EMBED_INVALID", $"{at}, field {f + 1}: the value must be 1-{DiscordMaxFieldValue} characters.");
                }

                total += (field.Name?.Length ?? 0) + (field.Value?.Length ?? 0);
            }

            total += (embed.Title?.Length ?? 0) + (embed.Description?.Length ?? 0) + (embed.Footer?.Length ?? 0);
        }

        if (total > DiscordMaxEmbedTotal)
        {
            Add("DISCORD_EMBED_INVALID", $"the embeds' text adds up to {total} characters; Discord allows {DiscordMaxEmbedTotal} in one message.");
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // Discord ids are 64-bit snowflakes written as decimal strings.
    private static bool IsSnowflake(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

    private static void ValidateWebhookStep(WebhookNotificationNode node, List<ValidationIssue> issues)
    {
        var config = node.Config;
        if (config is null)
        {
            return;
        }

        // Relative to what? Only an integration's base URL can answer that. A leading '/' is relative
        // too, though on Unix Uri reads it as an absolute file path.
        if (node.IntegrationRef is null && (!Uri.TryCreate(config.Url, UriKind.Absolute, out _) || config.Url.StartsWith('/')))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "WEBHOOK_URL_RELATIVE",
                $"Step '{node.Name}': '{config.Url}' is not an absolute URL. A relative URL needs an HTTP auth integration to be relative to.",
                node.NodeId));
        }

        if (config.TimeoutSeconds is < 1 or > WebhookNotificationConfig.MaxTimeoutSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "WEBHOOK_TIMEOUT_INVALID",
                $"Step '{node.Name}': the timeout must be 1 to {WebhookNotificationConfig.MaxTimeoutSeconds} seconds.",
                node.NodeId));
        }

        if (config.Headers is null)
        {
            return;
        }

        if (config.Headers.Count > WebhookNotificationConfig.MaxHeaders)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "WEBHOOK_HEADERS_TOO_MANY", $"Step '{node.Name}' sets more than {WebhookNotificationConfig.MaxHeaders} headers.", node.NodeId));
        }

        foreach (var (name, value) in config.Headers)
        {
            if (!IsHeaderToken(name) || value is null || value.Any(c => c is '\r' or '\n' or '\0'))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "WEBHOOK_HEADER_INVALID", $"Step '{node.Name}': header '{name}' is not a valid header name and value.", node.NodeId));
            }
            else if (ForbiddenWebhookHeaders.Contains(name))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "WEBHOOK_HEADER_FORBIDDEN",
                    $"Step '{node.Name}' may not set '{name}'. A credential belongs in an HTTP auth integration, and the rest are set by the request itself.",
                    node.NodeId));
            }
        }
    }

    // RFC 9110 token characters.
    private static bool IsHeaderToken(string name) =>
        name.Length is > 0 and <= 100 && name.All(c => c < 127 && (char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c)));

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
            // A kind with no descriptor has no port table to check against; ValidateNodeKinds already
            // reports it as an error, and listing each of its ports as unexpected would only add noise.
            if (!NodeKinds.TryGet(node.Kind, out NodeKindDescriptor? descriptor))
            {
                continue;
            }

            var expectedPorts = descriptor.ExpectedPorts(node);

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
            bool allowsOptionalErrorPort = descriptor.Category == NodeCategory.Action;

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

    /// <summary>The node's expected ports from its kind's descriptor; none for an unknown kind.</summary>
    private static IReadOnlyCollection<PortDefinition> GetExpectedPorts(BaseNode node) =>
        NodeKinds.TryGet(node.Kind, out NodeKindDescriptor? descriptor) ? descriptor.ExpectedPorts(node) : [];
}
