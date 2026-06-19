using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
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
            RunCompensationOnFailure: _runCompensationOnFailure);
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

    public WorkflowBuilder AddDeviceTrigger(string deviceRef, string eventName, WorkflowConcurrencyPolicy concurrencyPolicy, string? correlationExpression, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new DeviceTriggerNode(
            NodeId: id,
            Name: "Device Trigger",
            Ports: [new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new DeviceTriggerConfig(deviceRef, eventName, correlationExpression, concurrencyPolicy)));
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddDeviceTrigger(string deviceRef, string eventName, WorkflowConcurrencyPolicy concurrencyPolicy, out Guid nodeId)
    {
        return AddDeviceTrigger(deviceRef, eventName, concurrencyPolicy, null, out nodeId);
    }

    public WorkflowBuilder AddDeviceTrigger(string deviceRef, out Guid nodeId)
    {
        return AddDeviceTrigger(deviceRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out nodeId);
    }

    public WorkflowBuilder AddDeviceTrigger(string deviceRef)
    {
        return AddDeviceTrigger(deviceRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _);
    }

    public WorkflowBuilder AddManualTrigger(out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ManualTriggerNode(
            NodeId: id,
            Name: "Manual Trigger",
            Ports: [new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new ManualTriggerConfig(null)));
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddManualTrigger()
    {
        return AddManualTrigger(out _);
    }

    public WorkflowBuilder AddVariable(VariableScope scope, VariableOperation operation, string key, JsonElement value)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new VariableNode(
            NodeId: id,
            Name: $"Var {operation} {key}",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new VariableConfig(scope, operation, key, value)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddSendCommand(string deviceRef, string command, string? name = null, CompensationDeclaration? compensation = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SendCommandActionNode(
            NodeId: id,
            Name: name ?? command,
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new SendCommandConfig(deviceRef, command, null),
            Retry: null,
            OnFailure: null,
            Compensation: compensation));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddParallelForEach(string collectionKey, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ParallelForEachNode(
            NodeId: id,
            Name: "Parallel For Each",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Body, PortDirection.Output, "Body"), new PortDefinition(PortNames.Empty, PortDirection.Output, "Empty")],
            Config: new ParallelForEachConfig(collectionKey)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Body);
        return this;
    }

    public WorkflowBuilder AddForEach(string collectionKey, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ForEachNode(
            NodeId: id,
            Name: "For Each",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Body, PortDirection.Output, "Body"), new PortDefinition(PortNames.Done, PortDirection.Output, "Done")],
            Config: new ForEachConfig(collectionKey)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Body);
        return this;
    }

    public WorkflowBuilder AddJoin(JoinMode mode, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new JoinNode(
            NodeId: id,
            Name: "Join",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new JoinConfig(mode)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddDelay(TimeSpan duration)
    {
        return AddDelay(duration, out _);
    }

    public WorkflowBuilder AddDelay(TimeSpan duration, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new DelayNode(
            NodeId: id,
            Name: "Delay",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new DelayConfig(duration)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddWebhook(string method, string url, JsonElement? body, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new WebhookNotificationNode(
            NodeId: id,
            Name: "Webhook",
            Ports: [
                new PortDefinition(PortNames.In, PortDirection.Input, "In"),
                new PortDefinition(PortNames.Default, PortDirection.Output, "Out"),
                new PortDefinition(PortNames.Error, PortDirection.Output, "Error")
            ],
            Config: new WebhookNotificationConfig(url, method, body),
            Retry: new RetryPolicy(RetryStrategy.Exponential, TimeSpan.FromMilliseconds(500), null, null, 5, 0, [])));

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
            new(PortNames.In, PortDirection.Input, "In"),
            new(PortNames.Default, PortDirection.Output, "Out")
        };

        if (ttl.HasValue)
        {
            ports.Add(new(PortNames.Timeout, PortDirection.Output, "Timeout"));
        }

        _nodes.Add(new AwaitSignalNode(
            NodeId: id,
            Name: $"Wait {signalName}",
            Ports: ports,
            Config: new AwaitSignalConfig(signalName, null, ttl, ttl.HasValue ? "timeout" : null)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddFork(string[] branches, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;

        var ports = branches.Select(b => new PortDefinition(b, PortDirection.Output, b)).ToList();
        ports.Insert(0, new PortDefinition(PortNames.In, PortDirection.Input, "In"));

        _nodes.Add(new ForkNode(
            NodeId: id,
            Name: "Fork",
            Ports: ports,
            Config: new ForkConfig(branches)));

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddFailRun(string reason)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new FailRunNode(
            NodeId: id,
            Name: "Fail Run",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In")],
            Config: new FailRunConfig(reason)));

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddEnd()
    {
        var id = Guid.NewGuid();
        _nodes.Add(new EndNode(
            NodeId: id,
            Name: "End",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In")]));

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddSubWorkflow(Guid targetWorkflowRefId, string? correlationKey = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SubWorkflowNode(
            NodeId: id,
            Name: "SubWorkflow",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new SubWorkflowConfig(targetWorkflowRefId, correlationKey)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddLogicGate(string condition, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new LogicGateNode(
            NodeId: id,
            Name: "Logic Gate",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.True, PortDirection.Output, "True"), new PortDefinition(PortNames.False, PortDirection.Output, "False")],
            Config: new LogicGateConfig(condition)));

        ConnectToHead(id, PortNames.In);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddWebhookTrigger(string path, string method, WorkflowConcurrencyPolicy concurrencyPolicy, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new WebhookTriggerNode(
            NodeId: id,
            Name: "Webhook Trigger",
            Ports: [new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new WebhookTriggerConfig(path, method, null, concurrencyPolicy)));

        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddScheduleTrigger(string cron, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ScheduleTriggerNode(
            NodeId: id,
            Name: "Schedule Trigger",
            Ports: [new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new ScheduleTriggerConfig(cron, null)));

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

    public WorkflowBuilder AddLogicGate(string condition, Action<LogicGateScope> branches)
    {
        AddLogicGate(condition, out var gateId);

        var scope = new LogicGateScope(this, gateId);
        branches(scope);

        // After branching, the active head is ambiguous unless merged by a join.
        _head = null;
        return this;
    }

    public WorkflowBuilder AddFork(string[] branchNames, Action<ForkScope> branches)
    {
        var id = Guid.NewGuid();
        var ports = new List<PortDefinition> { new PortDefinition(PortNames.In, PortDirection.Input, "In") };
        foreach (var b in branchNames)
        {
            ports.Add(new PortDefinition(b, PortDirection.Output, b));
        }

        _nodes.Add(new ForkNode(
            NodeId: id,
            Name: "Fork",
            Ports: ports,
            Config: new ForkConfig(branchNames)));

        ConnectToHead(id, PortNames.In);

        var scope = new ForkScope(this, id);
        branches(scope);

        _head = null;
        return this;
    }

    public WorkflowBuilder AddParallelForEach(string collectionKey, Action<ForEachScope> loopBody)
    {
        AddParallelForEach(collectionKey, out var pfeId);

        var scope = new ForEachScope(this, pfeId);
        loopBody(scope);

        _head = (pfeId, PortNames.Empty);
        return this;
    }

    public WorkflowBuilder AddForEach(string collectionKey, Action<ForEachScope> loopBody)
    {
        AddForEach(collectionKey, out var loopId);

        var scope = new ForEachScope(this, loopId);
        loopBody(scope);

        _head = (loopId, PortNames.Done);
        return this;
    }

    public WorkflowBuilder AddJoin(JoinMode mode, params (Guid NodeId, string PortId)[] branchEnds)
    {
        AddJoin(mode, out var joinId);
        foreach (var end in branchEnds)
        {
            _edges.Add(new Edge(end, (joinId, PortNames.In)));
        }
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

    public WorkflowBuilder AddWebhook(string method, string url, JsonElement? body, Action<WebhookScope> webhookBranches)
    {
        AddWebhook(method, url, body, out var webhookId);
        var scope = new WebhookScope(this, webhookId);
        webhookBranches(scope);
        _head = null;
        return this;
    }

    public WorkflowBuilder AddAwaitSignal(string signalName, TimeSpan? ttl, Action<TimeoutScope> branches)
    {
        AddAwaitSignal(signalName, ttl, out var awaitId);
        var scope = new TimeoutScope(this, awaitId, ttl.HasValue);
        branches(scope);
        _head = null;
        return this;
    }
    public WorkflowBuilder AddWaitForHttp(TimeSpan ttl, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new Wbskt.Workflow.Abstraction.Models.Nodes.Controls.WaitForHttpNode(
            NodeId: id,
            Name: "Wait For Http",
            Ports: [new PortDefinition(PortNames.In, PortDirection.Input, "In"), new PortDefinition(PortNames.Default, PortDirection.Output, "Out")],
            Config: new Wbskt.Workflow.Abstraction.Models.Nodes.Controls.WaitForHttpConfig(ttl, null)));

        ConnectToHead(id, PortNames.In);
        _head = (id, PortNames.Default);
        return this;
    }

    public WorkflowBuilder AddWaitForHttp(TimeSpan ttl, Action<TimeoutScope> branches)
    {
        AddWaitForHttp(ttl, out var waitId);
        var scope = new TimeoutScope(this, waitId, true);
        branches(scope);
        _head = null;
        return this;
    }
}

public sealed class LogicGateScope(WorkflowBuilder builder, Guid gateId)
{
    public Guid GateId => gateId;

    public void OnTrue(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(gateId, PortNames.True);
        branch(builder);
    }

    public void OnFalse(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(gateId, PortNames.False);
        branch(builder);
    }
}

public sealed class ForkScope(WorkflowBuilder builder, Guid forkId)
{
    public void Branch(string branchName, Action<WorkflowBuilder> branch)
    {
        builder.SetHead(forkId, branchName);
        branch(builder);
    }
}

public sealed class ForEachScope(WorkflowBuilder builder, Guid loopId)
{
    public Guid LoopId => loopId;

    public void OnBody(Action<WorkflowBuilder> body)
    {
        builder.SetHead(loopId, PortNames.Body);
        body(builder);
    }

    public void OnDone(Action<WorkflowBuilder> done)
    {
        builder.SetHead(loopId, PortNames.Done);
        done(builder);
    }
}

public sealed class WebhookScope(WorkflowBuilder builder, Guid webhookId)
{
    public Guid WebhookId => webhookId;

    public void OnSuccess(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(webhookId, PortNames.Default);
        branch(builder);
    }

    public void OnError(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(webhookId, PortNames.Error);
        branch(builder);
    }
}

public sealed class TimeoutScope(WorkflowBuilder builder, Guid nodeId, bool hasTimeout)
{
    public Guid NodeId => nodeId;

    public void OnSuccess(Action<WorkflowBuilder> branch)
    {
        builder.SetHead(nodeId, PortNames.Default);
        branch(builder);
    }

    public void OnTimeout(Action<WorkflowBuilder> branch)
    {
        if (hasTimeout)
        {
            builder.SetHead(nodeId, PortNames.Timeout);
            branch(builder);
        }
    }
}
