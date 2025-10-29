import React from 'react';

interface TriggerConfigFormProps {
  nodeId: string;
  nodeType: string;
  // Add other props for configuration data and onChange handler
}

export const TriggerConfigForm: React.FC<TriggerConfigFormProps> = ({ nodeId, nodeType }) => {
  return (
    <div className="config-form">
      <h4>Trigger Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      {/* Placeholder for actual form fields */}
      <label>Trigger Specific Setting:</label>
      <input type="text" placeholder="e.g., Cron Schedule" />
    </div>
  );
};
