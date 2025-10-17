import type { Node } from '@reactflow/core';
import { StepIdentifier } from '../../../../common/enums/StepIdentifier';
import type { StepConfigurationBase } from '../../../../types/api';
import { IfConditionConfigForm } from './configs/IfConditionConfigForm';
import { SendEmailConfigForm } from './configs/SendEmailConfigForm';
import { TimedTriggerConfigForm } from './configs/TimedTriggerConfigForm';

interface ConfigPanelProps {
  selectedNode: Node | null;
  onConfigurationChange: (newConfig: StepConfigurationBase) => void;
}

export const ConfigPanel = ({ selectedNode, onConfigurationChange }: ConfigPanelProps) => {
  if (!selectedNode) {
    return (
      <div className="p-4 text-sm text-gray-500">
        Select a node to configure it.
      </div>
    );
  }

  const renderForm = () => {
    const config = selectedNode.data.configuration;
    const stepId = selectedNode.data.stepIdentifier as StepIdentifier;

    switch (stepId) {
      case StepIdentifier.Timed:
        return <TimedTriggerConfigForm configuration={config} onChange={onConfigurationChange} />;
      case StepIdentifier.ActionSendEmail:
        return <SendEmailConfigForm configuration={config} onChange={onConfigurationChange} />;
      case StepIdentifier.ModifierIfCondition:
        return <IfConditionConfigForm configuration={config} onChange={onConfigurationChange} />;
      default:
        return <p className="p-4 text-sm">This node is not configurable.</p>;
    }
  };

  return (
    <div>
      <div className="p-4 font-bold border-b">{selectedNode.data.label}</div>
      <div className="p-4">
        {renderForm()}
      </div>
    </div>
  );
};
