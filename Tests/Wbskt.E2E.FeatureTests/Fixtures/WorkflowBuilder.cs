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

    public WorkflowBuilder(string name, Guid? workflowRefId = null)
    {
        _name = name;
        _workflowRefId = workflowRefId ?? Guid.NewGuid();
    }

    public Guid WorkflowRefId => _workflowRefId;

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
            PublishedBy: 1);
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

    public WorkflowBuilder AddDeviceTrigger(string deviceRef, string eventName = "telemetry", WorkflowConcurrencyPolicy concurrencyPolicy = WorkflowConcurrencyPolicy.AllowParallel)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new DeviceTriggerNode(
            NodeId: id,
            Name: "Device Trigger",
            Ports: [new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new DeviceTriggerConfig(deviceRef, eventName, null, concurrencyPolicy)));
        _head = (id, "default");
        return this;
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

    public WorkflowBuilder AddSendCommand(string deviceRef, string command, string? name = null)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new SendCommandActionNode(
            NodeId: id,
            Name: name ?? command,
            Ports: [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("default", PortDirection.Output, "Out")],
            Config: new SendCommandConfig(deviceRef, command, null)));
            
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

    private void ConnectToHead(Guid targetNodeId, string targetPortId)
    {
        if (_head.HasValue)
        {
            _edges.Add(new Edge(_head.Value, (targetNodeId, targetPortId)));
        }
    }
}
