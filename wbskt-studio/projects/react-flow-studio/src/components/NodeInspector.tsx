import React from 'react';
import type { Node } from 'reactflow';
import { TriggerConfigForm } from './configs/TriggerConfigForm';
import { ActionConfigForm } from './configs/ActionConfigForm';
import { ConditionConfigForm } from './configs/ConditionConfigForm';

interface NodeInspectorProps {
  selectedNode: Node | null;
  onConfigChange: (nodeId: string, config: any) => void;
}

export const NodeInspector: React.FC<NodeInspectorProps> = ({ selectedNode, onConfigChange }) => {
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
        return (
          <TriggerConfigForm
            nodeId={selectedNode.id}
            nodeType={selectedNode.type}
            currentConfig={selectedNode.data.configuration}
            onConfigChange={(newConfig) => onConfigChange(selectedNode.id, newConfig)}
          />
        );
      case 'action':
        return (
          <ActionConfigForm
            nodeId={selectedNode.id}
            nodeType={selectedNode.type}
            currentConfig={selectedNode.data.configuration}
            onConfigChange={(newConfig) => onConfigChange(selectedNode.id, newConfig)}
          />
        );
      case 'condition':
        return (
          <ConditionConfigForm
            nodeId={selectedNode.id}
            nodeType={selectedNode.type}
            currentConfig={selectedNode.data.configuration}
            onConfigChange={(newConfig) => onConfigChange(selectedNode.id, newConfig)}
          />
        );
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
