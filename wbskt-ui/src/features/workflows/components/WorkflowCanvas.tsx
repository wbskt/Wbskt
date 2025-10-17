import { useState } from 'react';
import { ReactFlowProvider, type Node, type Edge, ReactFlow } from '@reactflow/core';
import { Controls } from '@reactflow/controls';
import { MiniMap } from '@reactflow/minimap';
import { TriggerNode } from './nodes/TriggerNode';
import { Background } from '@reactflow/background';

import { ActionNode } from './nodes/ActionNode';
import { ConditionNode } from './nodes/ConditionNode';

import '@reactflow/core/dist/style.css';
import '@reactflow/controls/dist/style.css';
import '@reactflow/minimap/dist/style.css';

const nodeTypes = {
  trigger: TriggerNode,
  action: ActionNode,
  condition: ConditionNode,
};

const initialNodes: Node[] = [
  { id: '1', type: 'trigger', position: { x: 100, y: 100 }, data: { label: 'Timed Trigger' } },
  { id: '2', type: 'condition', position: { x: 400, y: 100 }, data: { label: 'If Temp > 40' } },
  { id: '3', type: 'action', position: { x: 700, y: 50 }, data: { label: 'Send Alert' } },
  { id: '4', type: 'action', position: { x: 400, y: 250 }, data: { label: 'Log Normal Temp' } },
];

const initialEdges: Edge[] = [
  { id: 'e1-2', source: '1', target: '2' },
  { id: 'e2-3', source: '2', sourceHandle: 'yes', target: '3' },
  { id: 'e2-4', source: '2', sourceHandle: 'no', target: '4' },
];

export const WorkflowCanvas = () => {
  const [nodes] = useState(initialNodes);
  const [edges] = useState(initialEdges);

  return (
    <div style={{ height: '100%', width: '100%' }}>
      <ReactFlow nodes={nodes} edges={edges} fitView nodeTypes={nodeTypes}>
        <Background />
        <Controls />
        <MiniMap />
      </ReactFlow>
    </div>
  );
};

export const WorkflowCanvasWrapper = () => (
  <ReactFlowProvider>
    <WorkflowCanvas />
  </ReactFlowProvider>
);
