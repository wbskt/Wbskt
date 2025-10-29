import React from 'react';

interface ActionConfigFormProps {
  nodeId: string;
  nodeType: string;
  // Add other props for configuration data and onChange handler
}

export const ActionConfigForm: React.FC<ActionConfigFormProps> = ({ nodeId, nodeType }) => {
  return (
    <div className="config-form">
      <h4>Action Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      {/* Placeholder for actual form fields */}
      <label>Action Type:</label>
      <select>
        <option>Send Email</option>
        <option>Make HTTP Request</option>
      </select>
    </div>
  );
};
