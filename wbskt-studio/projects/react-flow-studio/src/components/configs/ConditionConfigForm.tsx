import React from 'react';

interface ConditionConfigFormProps {
  nodeId: string;
  nodeType: string;
  // Add other props for configuration data and onChange handler
}

export const ConditionConfigForm: React.FC<ConditionConfigFormProps> = ({ nodeId, nodeType }) => {
  return (
    <div className="config-form">
      <h4>Condition Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      {/* Placeholder for actual form fields */}
      <label>Condition:</label>
      <input type="text" placeholder="e.g., temperature > 25" />
    </div>
  );
};
