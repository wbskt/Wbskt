import type { Node, Edge } from 'reactflow';
import type { Workflow, WorkflowStep } from '../types';

export const serializeWorkflow = (workflowId: string, nodes: Node[], edges: Edge[]): Partial<Workflow> => {
  const steps: WorkflowStep[] = nodes.map((node, index) => {
    const onSuccessEdge = edges.find(e => e.source === node.id && !e.sourceHandle);
    const onFailureEdge = edges.find(e => e.source === node.id && e.sourceHandle === 'no');

    return {
      id: node.id, // This will be ignored by backend for new steps, but useful for updates
      workflowId: workflowId, // Placeholder, will be set by backend
      stepOrder: index,
      name: node.data.label,
      stepType: node.type === 'trigger' ? 'Trigger' : (node.type === 'condition' ? 'Modifier' : 'Action'),
      stepIdentifier: node.data.stepIdentifier || node.type, // Assuming stepIdentifier is stored in data
      stepConfiguration: JSON.stringify(node.data.configuration || {}),
      onSuccessStepId: onSuccessEdge ? onSuccessEdge.target : null,
      onFailureStepId: onFailureEdge ? onFailureEdge.target : null,
      PositionX: Math.round(node.position.x),
      PositionY: Math.round(node.position.y),
    };
  });

  // Placeholder for other workflow properties like name, description, etc.
  // These would typically come from a separate state or prop in the React app
  return {
    steps: steps,
    // Other workflow properties would be added here
  };
};

export const deserializeWorkflow = (workflow: Workflow) => {
  const nodes: Node[] = workflow.steps.map(step => ({
    id: step.id,
    type: step.stepType.toLowerCase(),
    position: { x: step.PositionX, y: step.PositionY },
    data: { label: step.name, configuration: JSON.parse(step.stepConfiguration), stepIdentifier: step.stepIdentifier },
  }));

  const edges: Edge[] = [];
  workflow.steps.forEach(step => {
    if (step.onSuccessStepId) {
      edges.push({ id: `e${step.id}-${step.onSuccessStepId}`, source: step.id, target: step.onSuccessStepId });
    }
    if (step.onFailureStepId) {
      edges.push({ id: `e${step.id}-${step.onFailureStepId}-no`, source: step.id, target: step.onFailureStepId, sourceHandle: 'no' });
    }
  });

  return { nodes, edges };
};
