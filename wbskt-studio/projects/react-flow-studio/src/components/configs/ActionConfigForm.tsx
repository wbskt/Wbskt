import React, { useState, useEffect } from 'react';

interface ActionConfigFormProps {
  nodeId: string;
  nodeType: string;
  currentConfig: any;
  onConfigChange: (config: any) => void;
}

export const ActionConfigForm: React.FC<ActionConfigFormProps> = ({ nodeId, nodeType, currentConfig, onConfigChange }) => {
  const [actionType, setActionType] = useState(currentConfig.actionType || 'Send Email');

  useEffect(() => {
    setActionType(currentConfig.actionType || 'Send Email');
  }, [currentConfig]);

  useEffect(() => {
    const newConfig = { actionType };
    onConfigChange(newConfig);
  }, [actionType, onConfigChange]);

  return (
    <div className="config-form">
      <h4>Action Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      <label>Action Type:</label>
      <select
        value={actionType}
        onChange={(e) => setActionType(e.target.value)}
      >
        <option>Send Email</option>
        <option>Make HTTP Request</option>
      </select>
    </div>
  );
};