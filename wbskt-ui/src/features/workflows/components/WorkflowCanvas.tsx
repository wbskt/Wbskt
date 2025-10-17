import { useCallback, useRef, useState } from 'react';
import { ReactFlowProvider, type Node, type Edge, ReactFlow, useReactFlow, addEdge, useEdgesState, useNodesState, type Connection } from '@reactflow/core';
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

interface WorkflowCanvasProps {
  onNodeSelected: (node: Node | null) => void;
}

export const WorkflowCanvas = ({ onNodeSelected }: WorkflowCanvasProps) => {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();

  const onConnect = useCallback(
    (params: Connection) => setEdges((eds) => addEdge(params, eds)),
    [setEdges]
  );

  const onNodeClick = (_: React.MouseEvent, node: Node) => {
    onNodeSelected(node);
  };

  const onPaneClick = () => {
    onNodeSelected(null);
  };

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
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onNodeClick={onNodeClick}
          onPaneClick={onPaneClick}
          fitView
          nodeTypes={nodeTypes}
        >
          <Background />
          <Controls />
          <MiniMap />
        </ReactFlow>
      </div>
    </div>
  );
};

export const WorkflowCanvasWrapper = ({ onNodeSelected }: WorkflowCanvasProps) => (
  <ReactFlowProvider>
    <WorkflowCanvas onNodeSelected={onNodeSelected} />
  </ReactFlowProvider>
);
