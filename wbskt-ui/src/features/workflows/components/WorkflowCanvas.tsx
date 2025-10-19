import type { Node } from '@reactflow/core';
import { type Edge, type NodeChange, type EdgeChange, type Connection, ReactFlow } from "@reactflow/core";
import { ActionNode } from "./nodes/ActionNode";
import { ConditionNode } from "./nodes/ConditionNode";
import { TriggerNode } from "./nodes/TriggerNode";
import { Controls } from '@reactflow/controls';
import { MiniMap } from '@reactflow/minimap';
import { Background } from '@reactflow/background';

import '@reactflow/core/dist/style.css';
import '@reactflow/controls/dist/style.css';
import '@reactflow/minimap/dist/style.css';

const nodeTypes = {
  trigger: TriggerNode,
  action: ActionNode,
  condition: ConditionNode,
};

interface WorkflowCanvasProps {
  nodes: Node[];
  edges: Edge[];
  onNodesChange: (changes: NodeChange[]) => void;
  onEdgesChange: (changes: EdgeChange[]) => void;
  onConnect: (connection: Connection) => void;
  onNodeClick: (event: React.MouseEvent, node: Node) => void;
  onPaneClick: () => void;
}

export const WorkflowCanvas = ({ 
    nodes, 
    edges, 
    onNodesChange, 
    onEdgesChange, 
    onConnect, 
    onNodeClick, 
    onPaneClick 
}: WorkflowCanvasProps) => {
  return (
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
  );
};