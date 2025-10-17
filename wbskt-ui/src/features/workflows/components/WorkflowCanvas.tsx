import { useRef, useState } from 'react';
import { ReactFlowProvider, type Node, type Edge, ReactFlow, useReactFlow } from '@reactflow/core';
import { Controls } from '@reactflow/controls';
import { MiniMap } from '@reactflow/minimap';
import { TriggerNode } from './nodes/TriggerNode';
import { Background } from '@reactflow/background';

import { ActionNode } from './nodes/ActionNode';
import { ConditionNode } from './nodes/ConditionNode';

import '@reactflow/core/dist/style.css';
import '@reactflow/controls/dist/style.css';
import '@reactflow/minimap/dist/style.css';
import { useDrop } from 'react-dnd';
import { DRAGGABLE_NODE_TYPE } from './panels/DraggableNode';

const nodeTypes = {
  trigger: TriggerNode,
  action: ActionNode,
  condition: ConditionNode,
};

const initialNodes: Node[] = [];
const initialEdges: Edge[] = [];

export const WorkflowCanvas = () => {
  const [nodes, setNodes] = useState<Node[]>(initialNodes);
  const [edges, setEdges] = useState<Edge[]>(initialEdges);
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();
  const [{ isOver }, drop] = useDrop({
    accept: DRAGGABLE_NODE_TYPE,
    drop: (item: { nodeType: string; label: string; stepIdentifier: string }, monitor) => {
      const offset = monitor.getClientOffset();
      if (offset && reactFlowWrapper.current) {
        const bounds = reactFlowWrapper.current.getBoundingClientRect();
        const position = project({ x: offset.x - bounds.left, y: offset.y - bounds.top });

        const newNode: Node = {
          id: crypto.randomUUID(),
          type: item.nodeType,
          position,
          data: { label: item.label, stepIdentifier: item.stepIdentifier },
        };

        setNodes((nds) => nds.concat(newNode));
      }
    },
    collect: (monitor) => ({ isOver: !!monitor.isOver() }),
  });

  return (
    <div ref={reactFlowWrapper} style={{ height: '100%', width: '100%' }}>
      <div ref={drop as unknown as React.Ref<HTMLDivElement>} style={{ height: '100%', width: '100%' }}>
        <ReactFlow nodes={nodes} edges={edges} fitView nodeTypes={nodeTypes}>
          <Background />
          <Controls />
          <MiniMap />
        </ReactFlow>
      </div>
    </div>
  );
};

export const WorkflowCanvasWrapper = () => (
  <ReactFlowProvider>
    <WorkflowCanvas />
  </ReactFlowProvider>
);
