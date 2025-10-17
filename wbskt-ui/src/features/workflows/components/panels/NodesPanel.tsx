import { DraggableNode } from './DraggableNode';
import { StepIdentifier } from '../../../../common/enums/StepIdentifier';

const availableNodes = [
  { group: 'Triggers', type: 'trigger', label: 'Timed Trigger', identifier: StepIdentifier.Timed },
  { group: 'Triggers', type: 'trigger', label: 'Webhook', identifier: StepIdentifier.Webhook },
  { group: 'Actions', type: 'action', label: 'Log Message', identifier: StepIdentifier.ActionLog },
  { group: 'Actions', type: 'action', label: 'Send Email', identifier: StepIdentifier.ActionSendEmail },
  { group: 'Actions', type: 'action', label: 'HTTP Request', identifier: StepIdentifier.ActionMakeHttpRequest },
  { group: 'Modifiers', type: 'condition', label: 'If Condition', identifier: StepIdentifier.ModifierIfCondition },
];

export const NodesPanel = () => {
  const groupedNodes = availableNodes.reduce((acc, node) => {
    (acc[node.group] = acc[node.group] || []).push(node);
    return acc;
  }, {} as Record<string, typeof availableNodes>);

  return (
    <div className="p-4">
      {Object.entries(groupedNodes).map(([group, nodes]) => (
        <div key={group} className="mb-4">
          <h3 className="font-bold text-gray-700 mb-2">{group}</h3>
          <div>
            {nodes.map((node) => (
              <DraggableNode
                key={node.identifier}
                label={node.label}
                nodeType={node.type}
                stepIdentifier={node.identifier.toString()}
              />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
};
