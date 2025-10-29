import React from 'react';
import type { Node } from 'reactflow';
import { TriggerConfigForm } from './configs/TriggerConfigForm';
import { ActionConfigForm } from './configs/ActionConfigForm';
import { ConditionConfigForm } from './configs/ConditionConfigForm';

interface NodeInspectorProps {
  selectedNode: Node | null;
}

export const NodeInspector: React.FC<NodeInspectorProps> = ({ selectedNode }) => {
  if (!selectedNode) {
    return (
      <div className="node-inspector">
        <p>Select a node to view its properties.</p>
      </div>
    );
  }

  const renderConfigForm = () => {
    switch (selectedNode.type) {
      case 'trigger':
        return <TriggerConfigForm nodeId={selectedNode.id} nodeType={selectedNode.type} />;
      case 'action':
        return <ActionConfigForm nodeId={selectedNode.id} nodeType={selectedNode.type} />;
      case 'condition':
        return <ConditionConfigForm nodeId={selectedNode.id} nodeType={selectedNode.type} />;
      default:
        return <p>No specific configuration for this node type.</p>;
    }
  };

  return (
    <div className="node-inspector">
      <h3>Node Properties</h3>
      <p>ID: {selectedNode.id}</p>
      <p>Type: {selectedNode.type}</p>
      <p>Label: {selectedNode.data.label}</p>
      {renderConfigForm()}
    </div>
  );
};