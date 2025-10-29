import React, { useState, useEffect } from 'react';

interface ConditionConfigFormProps {
  nodeId: string;
  nodeType: string;
  currentConfig: any;
  onConfigChange: (config: any) => void;
}

export const ConditionConfigForm: React.FC<ConditionConfigFormProps> = ({ nodeId, nodeType, currentConfig, onConfigChange }) => {
  const [conditionExpression, setConditionExpression] = useState(currentConfig.conditionExpression || '');

  useEffect(() => {
    setConditionExpression(currentConfig.conditionExpression || '');
  }, [currentConfig]);

  useEffect(() => {
    const newConfig = { conditionExpression };
    onConfigChange(newConfig);
  }, [conditionExpression, onConfigChange]);

  return (
    <div className="config-form">
      <h4>Condition Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      <label>Condition:</label>
      <input
        type="text"
        placeholder="e.g., temperature > 25"
        value={conditionExpression}
        onChange={(e) => setConditionExpression(e.target.value)}
      />
    </div>
  );
};