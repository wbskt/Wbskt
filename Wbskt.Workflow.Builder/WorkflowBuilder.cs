using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Builder;

public sealed class WorkflowBuilder
{
    private readonly Guid _workflowRefId;
    private readonly string _name;
    private readonly List<BaseNode> _nodes = [];
    private readonly List<Edge> _edges = [];
    private (Guid NodeId, string PortId)? _head;
    private bool _runCompensationOnFailure;
    private decimal? _creditBudget;

    public WorkflowBuilder(string name, Guid? workflowRefId = null)
    {
        _name = name;
        _workflowRefId = workflowRefId ?? Guid.NewGuid();
    }

    public Guid WorkflowRefId => _workflowRefId;

    public WorkflowBuilder EnableCompensationOnFailure(bool enable = true)
    {
        _runCompensationOnFailure = enable;
        return this;
    }

    /// <summary>Caps the compute a single run of this workflow may consume before it goes OutOfCredits.</summary>
    public WorkflowBuilder WithCreditBudget(decimal creditBudget)
    {
        _creditBudget = creditBudget;
        return this;
    }

    public WorkflowDefinition Build()
    {
        return new WorkflowDefinition(
            WorkflowRefId: _workflowRefId,
            Version: 1,
            WorkspaceId: 1,
            Name: _name,
            Description: null,
            IsEnabled: true,
            Nodes: _nodes,
            Edges: _edges,
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1,
            RunCompensationOnFailure: _runCompensationOnFailure,
            CreditBudget: _creditBudget);
    }

    public WorkflowBuilder SetHead(Guid nodeId, string portId)
    {
        _head = (nodeId, portId);
        return this;
    }

    public WorkflowBuilder ClearHead()
    {
        _head = null;
        return this;
    }

    public WorkflowBuilder Connect(Guid sourceNodeId, string sourcePortId, Guid targetNodeId, string targetPortId)
    {
        _edges.Add(new Edge((sourceNodeId, sourcePortId), (targetNodeId, targetPortId)));
        return this;
    }

    public WorkflowBuilder AddClientTrigger(string clientRef, string type, WorkflowConcurrencyPolicy concurrencyPolicy, string? correlationExpression, out Guid nodeId, string? name = null, WorkflowExpression? filter = null, int holdSeconds = 0)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ClientTriggerNode {
            NodeId = id,
            Name = name ?? "Device Trigger",
            Ports = [new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new ClientTriggerConfig { ClientRef = clientRef, Type = type, CorrelationKey = correlationExpression, ConcurrencyPolicy = concurrencyPolicy, Filter = filter, HoldSeconds = holdSeconds } });
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddClientPresenceTrigger(string clientRef, ClientPresenceState state, int forSeconds, out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ClientPresenceTriggerNode {
            NodeId = id,
            Name = name ?? "Device Presence Trigger",
            Ports = [new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new ClientPresenceTriggerConfig { ClientRef = clientRef, State = state, ForSeconds = forSeconds } });
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddClientTrigger(string clientRef, string type, WorkflowConcurrencyPolicy concurrencyPolicy, out Guid nodeId)
    {
        return AddClientTrigger(clientRef, type, concurrencyPolicy, null, out nodeId);
    }

    public WorkflowBuilder AddTelemetryClientTrigger(string clientRef)
    {
        return AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _);
    }

    public WorkflowBuilder AddManualTrigger(out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ManualTriggerNode {
            NodeId = id,
            Name = name ?? "Manual Trigger",
            Ports = [new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new ManualTriggerConfig { Description = null } });
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddManualTrigger()
    {
        return AddManualTrigger(out _);
    }

    public WorkflowBuilder AddVariable(VariableScope scope, VariableOperation operation, string key, JsonElement value, string? name = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new VariableNode {
            NodeId = id,
            Name = name ?? $"Var {operation} {key}",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new VariableConfig { Scope = scope, Op = operation, Var = key, Value = value } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddClientMessage(string clientRef, string messageType, string? name = null, CompensationDeclaration? compensation = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SendClientMessageNode {
            NodeId = id,
            Name = name ?? messageType,
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new SendClientMessageConfig { ClientRef = clientRef, Type = messageType, Payload = null },
            Retry = null,
            OnFailure = null,
            Compensation = compensation });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    /// <summary>
    /// Raises a toast to everyone watching the workspace. Unlike <see cref="AddClientMessage"/> this
    /// has no device target - it goes to the workspace's dashboard connections.
    /// </summary>
    public WorkflowBuilder AddToast(string title, string message, string? name = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new ToastNotificationNode {
            NodeId = id,
            Name = name ?? title,
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new ToastConfig { Title = title, Message = message },
            Retry = null,
            OnFailure = null,
            Compensation = null });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    /// <remarks>
    /// No credentials here: the SMTP relay is host configuration (<c>WorkflowEngine:Email</c>), because a
    /// definition is readable by the whole workspace and frozen into every published version.
    /// </remarks>
    public WorkflowBuilder AddEmail(string to, string subject, string body, string? name = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new EmailNotificationNode {
            NodeId = id,
            Name = name ?? subject,
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new EmailConfig { To = to, Subject = subject, Body = body },
            Retry = null,
            OnFailure = null,
            Compensation = null });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    /// <remarks>The bot token is host configuration (<c>WorkflowEngine:Telegram</c>), not node config.</remarks>
    public WorkflowBuilder AddTelegram(string chatId, string message, string? name = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new TelegramNotificationNode {
            NodeId = id,
            Name = name ?? "Telegram",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new TelegramConfig { ChatId = chatId, Message = message },
            Retry = null,
            OnFailure = null,
            Compensation = null });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddParallelForEach(string collectionKey, out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ParallelForEachNode {
            NodeId = id,
            Name = name ?? "Parallel For Each",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Body, Direction = PortDirection.Output, Label = "Body" }, new PortDefinition { PortId = PortNames.Empty, Direction = PortDirection.Output, Label = "Empty" }],
            Config = new ParallelForEachConfig { Collection = collectionKey } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Body);
        return this;
    }

    public WorkflowBuilder AddForEach(string collectionKey, out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ForEachNode {
            NodeId = id,
            Name = name ?? "For Each",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Body, Direction = PortDirection.Output, Label = "Body" }, new PortDefinition { PortId = PortNames.Done, Direction = PortDirection.Output, Label = "Done" }],
            Config = new ForEachConfig { Collection = collectionKey } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Body);
        return this;
    }

    public WorkflowBuilder AddJoin(JoinMode mode, out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new JoinNode {
            NodeId = id,
            Name = name ?? "Join",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new JoinConfig { Mode = mode } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddDelay(TimeSpan duration)
    {
        return AddDelay(duration, out _);
    }

    public WorkflowBuilder AddDelay(TimeSpan duration, out Guid nodeId, string? name = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new DelayNode {
            NodeId = id,
            Name = name ?? "Delay",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new DelayConfig { Duration = duration } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddWebhook(string method, string url, JsonElement? body, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new WebhookNotificationNode {
            NodeId = id,
            Name = "Webhook",
            Ports = [
                new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" },
                new PortDefinition { PortId = PortNames.Error, Direction = PortDirection.Output, Label = "Error" }
            ],
            Config = new WebhookNotificationConfig { Url = url, Method = method, Body = body },
            Retry = new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromMilliseconds(500), Factor = null, MaxDelay = null, MaxAttempts = 5, JitterPct = 0, RetryOn = [] } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddAwaitSignal(string signalName, TimeSpan? ttl, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;

        var ports = new List<PortDefinition>
        {
            new() { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" },
            new() { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }
        };

        if (ttl.HasValue)
        {
            ports.Add(new() { PortId = PortNames.Timeout, Direction = PortDirection.Output, Label = "Timeout" });
        }

        _nodes.Add(new AwaitSignalNode {
            NodeId = id,
            Name = $"Wait {signalName}",
            Ports = ports,
            Config = new AwaitSignalConfig { SignalName = signalName, Correlation = null, Ttl = ttl } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddFork(string[] branches, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;

        var ports = branches.Select(b => new PortDefinition { PortId = b, Direction = PortDirection.Output, Label = b }).ToList();
        ports.Insert(0, new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" });

        _nodes.Add(new ForkNode {
            NodeId = id,
            Name = "Fork",
            Ports = ports,
            Config = new ForkConfig { Branches = branches } });

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddFailRun(string reason)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new FailRunNode {
            NodeId = id,
            Name = "Fail Run",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }],
            Config = new FailRunConfig { Reason = reason } });

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddEnd()
    {
        var id = Guid.NewGuid();
        _nodes.Add(new EndNode {
            NodeId = id,
            Name = "End",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }] });

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    /// <param name="input">
    /// Values evaluated against the calling branch and merged into the child's <c>$trigger.body</c>.
    /// <c>parentRunRefId</c> and <c>correlationKey</c> are written by the engine and rejected here.
    /// </param>
    public WorkflowBuilder AddSubWorkflow(Guid targetWorkflowRefId, string? correlationKey = null, IReadOnlyDictionary<string, WorkflowExpression>? input = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SubWorkflowNode {
            NodeId = id,
            Name = "SubWorkflow",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new SubWorkflowConfig { WorkflowRefId = targetWorkflowRefId, CorrelationKey = correlationKey, Input = input } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    /// <summary>
    /// Adds a Logic gate whose condition is a structured expression - the form that supports
    /// comparisons and functions, e.g. <c>reading.temperature &gt; 30</c>.
    /// </summary>
    public WorkflowBuilder AddLogicGate(WorkflowExpression condition, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new LogicGateNode {
            NodeId = id,
            Name = "Logic Gate",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.True, Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = PortNames.False, Direction = PortDirection.Output, Label = "False" }],
            Config = new LogicGateConfig { Condition = condition } });

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    /// <summary>
    /// Legacy string form. The string is NOT parsed as an expression - it is interpreted exactly as
    /// the pre-structured engine did: "true"/"false" is a literal, anything else is a branch-state
    /// path holding a boolean. Use the <see cref="WorkflowExpression"/> overload to compare values.
    /// </summary>
    public WorkflowBuilder AddLogicGate(string condition, out Guid nodeId)
    {
        return AddLogicGate(LogicConditionJsonConverter.FromLegacyString(condition), out nodeId);
    }

    public WorkflowBuilder AddWebhookTrigger(string path, string method, WorkflowConcurrencyPolicy concurrencyPolicy, out Guid nodeId, WorkflowExpression? filter = null, string? secret = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new WebhookTriggerNode {
            NodeId = id,
            Name = "Webhook Trigger",
            Ports = [new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new WebhookTriggerConfig { Path = path, Method = method, CorrelationKey = null, ConcurrencyPolicy = concurrencyPolicy, Filter = filter, Secret = secret } });

        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddScheduleTrigger(string cron, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ScheduleTriggerNode {
            NodeId = id,
            Name = "Schedule Trigger",
            Ports = [new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new ScheduleTriggerConfig { Cron = cron, CorrelationKey = null } });

        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddEdge(Guid sourceNodeId, string sourcePort, Guid targetNodeId, string targetPort = PortNames.In)
    {
        _edges.Add(new Edge((sourceNodeId, sourcePort), (targetNodeId, targetPort)));
        return this;
    }

    private void ConnectToHead(Guid targetNodeId, string targetPortId)
    {
        if (_head.HasValue)
        {
            _edges.Add(new Edge(_head.Value, (targetNodeId, targetPortId)));
        }
    }

    // --- FLUENT EXTENSIONS ---

    public (Guid NodeId, string PortId)? CurrentHead => _head;

    public WorkflowBuilder AddLogicGate(string condition, Action<LogicGateScope> branches, JoinMode? joinMode = null)
    {
        return AddLogicGate(LogicConditionJsonConverter.FromLegacyString(condition), branches, joinMode);
    }

    public WorkflowBuilder AddLogicGate(WorkflowExpression condition, Action<LogicGateScope> branches, JoinMode? joinMode = null)
    {
        AddLogicGate(condition, out var gateId);

        var scope = new LogicGateScope(this, gateId);
        branches(scope);

        if (joinMode.HasValue && scope.Tails.Count > 0)
        {
            AddJoin(joinMode.Value, scope.Tails.ToArray());
        }
        else
        {
            _head = null;
        }
        return this;
    }

    public WorkflowBuilder AddFork(string[] branchNames, Action<ForkScope> branches, JoinMode? joinMode = null)
    {
        var id = Guid.NewGuid();
        var ports = new List<PortDefinition> { new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" } };
        foreach (var b in branchNames)
        {
            ports.Add(new PortDefinition { PortId = b, Direction = PortDirection.Output, Label = b });
        }

        _nodes.Add(new ForkNode {
            NodeId = id,
            Name = "Fork",
            Ports = ports,
            Config = new ForkConfig { Branches = branchNames } });

        ConnectToHead(id, PortNames.In);

        var scope = new ForkScope(this, id);
        branches(scope);

        if (joinMode.HasValue && scope.Tails.Count > 0)
        {
            AddJoin(joinMode.Value, scope.Tails.ToArray());
        }
        else
        {
            _head = null;
        }
        return this;
    }

    /// <summary>
    /// Fans the collection out, one branch per item, and converges them on a Join.
    ///
    /// The Join is not optional: a ParallelForEach cohort has nowhere to converge without one, and
    /// the engine rejects such a definition at fan-out with PFE_NO_JOIN. If the body ends without a
    /// tail to wire up - e.g. it ends in a Fork that was given no join mode of its own - no Join can
    /// be added here and the definition will be rejected; give the inner construct a join mode.
    /// </summary>
    public WorkflowBuilder AddParallelForEach(string collectionKey, Action<ForEachScope> loopBody, JoinMode joinMode = JoinMode.All)
    {
        AddParallelForEach(collectionKey, out var pfeId);

        var scope = new ForEachScope(this, pfeId);
        loopBody(scope);

        if (scope.BodyTail is { } bodyTail)
        {
            // Leaves the head on the Join's "default" port, so whatever follows runs once, after the
            // cohort converges - rather than once per item.
            AddJoin(joinMode, bodyTail);
        }
        else
        {
            _head = (pfeId, PortNames.Empty);
        }

        return this;
    }

    public WorkflowBuilder AddForEach(string collectionKey, Action<ForEachScope> loopBody)
    {
        AddForEach(collectionKey, out var loopId);

        var scope = new ForEachScope(this, loopId);
        loopBody(scope);

        // Close the loop. ForEach is sequential: the body's tail returns to the ForEach node, which
        // then hands out the next item or leaves via "done". Without this back-edge the body would
        // run once and the graph would be a straight line, not a loop.
        if (scope.BodyTail is { } bodyTail)
        {
            AddEdge(bodyTail.NodeId, bodyTail.PortId, loopId, PortNames.In);
        }

        _head = (loopId, PortNames.Done);
        return this;
    }

    public WorkflowBuilder AddJoin(JoinMode mode, params (Guid NodeId, string PortId)[] branchEnds)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new JoinNode
        {
            NodeId = id,
            Name = "Join",
            Ports = [new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" }],
            Config = new JoinConfig { Mode = mode }
        });

        // Wire each branch tail to join.In — do NOT call ConnectToHead here to avoid
        // double-wiring the last tail (ConnectToHead would add it once, foreach adds it again).
        foreach (var end in branchEnds)
        {
            _edges.Add(new Edge(end, (id, PortNames.In)));
        }

        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowDefinition BuildAndValidate()
    {
        var def = Build();
        var validator = new Wbskt.Workflow.Abstraction.Validation.WorkflowValidator();
        var result = validator.Validate(def);
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"Workflow definition is invalid: {string.Join("; ", result.Issues)}");
        }
        return def;
    }

    public WorkflowBuilder AddWebhook(string method, string url, JsonElement? body, Action<WebhookScope> webhookBranches, JoinMode? joinMode = null)
    {
        AddWebhook(method, url, body, out var webhookId);
        var scope = new WebhookScope(this, webhookId);
        webhookBranches(scope);
        
        if (joinMode.HasValue && scope.Tails.Count > 0)
        {
            AddJoin(joinMode.Value, scope.Tails.ToArray());
        }
        else
        {
            _head = null;
        }
        return this;
    }

    public WorkflowBuilder AddAwaitSignal(string signalName, TimeSpan? ttl, Action<TimeoutScope> branches, JoinMode? joinMode = null)
    {
        AddAwaitSignal(signalName, ttl, out var awaitId);
        var scope = new TimeoutScope(this, awaitId, ttl.HasValue);
        branches(scope);
        
        if (joinMode.HasValue && scope.Tails.Count > 0)
        {
            AddJoin(joinMode.Value, scope.Tails.ToArray());
        }
        else
        {
            _head = null;
        }
        return this;
    }
    public WorkflowBuilder AddWaitForHttp(TimeSpan ttl, out Guid nodeId, string? token = null)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new Wbskt.Workflow.Abstraction.Models.Nodes.Controls.WaitForHttpNode {
            NodeId = id,
            Name = "Wait For Http",
            Ports = [
                new PortDefinition { PortId = PortNames.In, Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Output, Label = "Out" },
                new PortDefinition { PortId = PortNames.Timeout, Direction = PortDirection.Output, Label = "Timeout" }
            ],
            Config = new Wbskt.Workflow.Abstraction.Models.Nodes.Controls.WaitForHttpConfig { Ttl = ttl, Token = token } });

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddWaitForHttp(TimeSpan ttl, Action<TimeoutScope> branches, JoinMode? joinMode = null, string? token = null)
    {
        AddWaitForHttp(ttl, out var waitId, token);
        var scope = new TimeoutScope(this, waitId, true);
        branches(scope);
        
        if (joinMode.HasValue && scope.Tails.Count > 0)
        {
            AddJoin(joinMode.Value, scope.Tails.ToArray());
        }
        else
        {
            _head = null;
        }
        return this;
    }
}

public sealed class LogicGateScope(WorkflowBuilder builder, Guid gateId)
{
    private readonly List<(Guid NodeId, string PortId)> _tails = [];
    public IReadOnlyCollection<(Guid NodeId, string PortId)> Tails => _tails;

    public Guid GateId => gateId;

    public void OnTrue(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(gateId, PortNames.True);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }

    public void OnFalse(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(gateId, PortNames.False);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }
}

public sealed class ForkScope(WorkflowBuilder builder, Guid forkId)
{
    private readonly List<(Guid NodeId, string PortId)> _tails = [];
    public IReadOnlyCollection<(Guid NodeId, string PortId)> Tails => _tails;

    public void Branch(string branchName, Action<WorkflowBuilder> branch)
    {
        builder.SetHead(forkId, branchName);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }
}

public sealed class ForEachScope(WorkflowBuilder builder, Guid loopId)
{
    public Guid LoopId => loopId;

    /// <summary>
    /// Where the body ended, so the loop can be closed with a back-edge to the ForEach node.
    /// Tracked separately from the builder head because <see cref="OnDone"/> moves the head onto the
    /// after-loop path.
    /// </summary>
    public (Guid NodeId, string PortId)? BodyTail { get; private set; }

    public void OnBody(Action<WorkflowBuilder> body)
    {
        builder.SetHead(loopId, PortNames.Body);
        body(builder);
        BodyTail = builder.CurrentHead;
    }

    public void OnDone(Action<WorkflowBuilder> done)
    {
        builder.SetHead(loopId, PortNames.Done);
        done(builder);
    }
}

public sealed class WebhookScope(WorkflowBuilder builder, Guid webhookId)
{
    private readonly List<(Guid NodeId, string PortId)> _tails = [];
    public IReadOnlyCollection<(Guid NodeId, string PortId)> Tails => _tails;

    public Guid WebhookId => webhookId;

    public void OnSuccess(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(webhookId, PortNames.Default);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }

    public void OnError(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(webhookId, PortNames.Error);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }
}

public sealed class TimeoutScope(WorkflowBuilder builder, Guid nodeId, bool hasTimeout)
{
    private readonly List<(Guid NodeId, string PortId)> _tails = [];
    public IReadOnlyCollection<(Guid NodeId, string PortId)> Tails => _tails;

    public Guid NodeId => nodeId;

    public void OnSuccess(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(nodeId, PortNames.Default);
        branch(builder);
        if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
    }

    public void OnTimeout(Action<WorkflowBuilder> branch)
    {
        if (hasTimeout)
        {
            builder.SetHead(nodeId, PortNames.Timeout);
            branch(builder);
            if (builder.CurrentHead.HasValue) _tails.Add(builder.CurrentHead.Value);
        }
    }
}
