import React, { useState, useEffect } from 'react';

interface TriggerConfigFormProps {
  nodeId: string;
  nodeType: string;
  currentConfig: any;
  onConfigChange: (config: any) => void;
}

export const TriggerConfigForm: React.FC<TriggerConfigFormProps> = ({ nodeId, nodeType, currentConfig, onConfigChange }) => {
  const [triggerSetting, setTriggerSetting] = useState(currentConfig.triggerSetting || '');

  useEffect(() => {
    setTriggerSetting(currentConfig.triggerSetting || '');
  }, [currentConfig]);

  useEffect(() => {
    const newConfig = { triggerSetting };
    onConfigChange(newConfig);
  }, [triggerSetting, onConfigChange]);

  return (
    <div className="config-form">
      <h4>Trigger Node Configuration</h4>
      <p>Node ID: {nodeId}</p>
      <p>Node Type: {nodeType}</p>
      <label>Trigger Specific Setting:</label>
      <input
        type="text"
        placeholder="e.g., Cron Schedule"
        value={triggerSetting}
        onChange={(e) => setTriggerSetting(e.target.value)}
      />
    </div>
  );
};