using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.E2E.FeatureTests.Fixtures;

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
            Ports: [new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new DeviceTriggerConfig(deviceRef, eventName, correlationExpression, concurrencyPolicy)));
        _head = (id, "default");
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
            Ports: [new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new ManualTriggerConfig(null)));
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddVariable(VariableScope scope, VariableOperation operation, string key, JsonElement value)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new VariableNode(
            NodeId: id,
            Name: $"Var {operation} {key}",
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new VariableConfig(scope, operation, key, value)));
        
        ConnectToHead(id, "in");
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddSendCommand(string deviceRef, string command, string? name = null, CompensationDeclaration? compensation = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SendCommandActionNode(
            NodeId: id,
            Name: name ?? command,
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new SendCommandConfig(deviceRef, command, null),
            Retry: null,
            OnFailure: null,
            Compensation: compensation));
            
        ConnectToHead(id, "in");
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddParallelForEach(string collectionKey, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ParallelForEachNode(
            NodeId: id,
            Name: "Parallel For Each",
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("body", PortDirection.Output, "Body")],
            Config: new ParallelForEachConfig(collectionKey)));
            
        ConnectToHead(id, "in");
        _head = (id, "body");
        return this;
    }

    public WorkflowBuilder AddForEach(string collectionKey, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new ForEachNode(
            NodeId: id,
            Name: "For Each",
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("body", PortDirection.Output, "Body"), new PortDefinition("done", PortDirection.Output, "Done")],
            Config: new ForEachConfig(collectionKey)));
            
        ConnectToHead(id, "in");
        _head = (id, "body");
        return this;
    }

    public WorkflowBuilder AddJoin(JoinMode mode, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        _nodes.Add(new JoinNode(
            NodeId: id,
            Name: "Join",
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new JoinConfig(mode)));
            
        ConnectToHead(id, "in");
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddDelay(TimeSpan duration)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new DelayNode(
            NodeId: id,
            Name: "Delay",
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new DelayConfig(duration)));
            
        ConnectToHead(id, "in");
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddAwaitSignal(string signalName, TimeSpan? ttl, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        
        var ports = new List<PortDefinition>
        {
            new("in", PortDirection.Input, "In"),
            new("default", PortDirection.Output, "Out")
        };

        if (ttl.HasValue)
        {
            ports.Add(new("timeout", PortDirection.Output, "Timeout"));
        }

        _nodes.Add(new AwaitSignalNode(
            NodeId: id,
            Name: $"Wait {signalName}",
            Ports: ports,
            Config: new AwaitSignalConfig(signalName, null, ttl, ttl.HasValue ? "timeout" : null)));
            
        ConnectToHead(id, "in");
        _head = (id, "default");
        return this;
    }

    public WorkflowBuilder AddFork(string[] branches, out Guid nodeId)
    {
        var id = Guid.NewGuid();
        nodeId = id;
        
        var ports = branches.Select(b => new PortDefinition(b, PortDirection.Output, b)).ToList();
        ports.Insert(0, new PortDefinition("in", PortDirection.Input, "In"));
        
        _nodes.Add(new ForkNode(
            NodeId: id,
            Name: "Fork",
            Ports: ports,
            Config: new ForkConfig(branches)));
            
        ConnectToHead(id, "in");
        _head = null;
        return this;
    }

    public WorkflowBuilder AddFailRun(string reason)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new FailRunNode(
            NodeId: id,
            Name: "Fail Run",
            Ports: [new PortDefinition("in", PortDirection.Input, "In")],
            Config: new FailRunConfig(reason)));
            
        ConnectToHead(id, "in");
        _head = null;
        return this;
    }

    private void ConnectToHead(Guid targetNodeId, string targetPortId)
    {
        if (_head.HasValue)
        {
            _edges.Add(new Edge(_head.Value, (targetNodeId, targetPortId)));
        }
    }
}
