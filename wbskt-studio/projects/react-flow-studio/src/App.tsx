import React, { useState, useCallback, useEffect, useRef } from 'react';
import ReactFlow, { Controls, Background, MiniMap, ReactFlowProvider, useNodesState, useEdgesState, useReactFlow, type Node, BackgroundVariant } from 'reactflow';
import 'reactflow/dist/style.css';
import './index.scss';
import { DndProvider, useDrop } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { NodeLibrary, DRAGGABLE_NODE_TYPE } from './components/NodeLibrary';
import { NodeInspector } from './components/NodeInspector';

interface AppProps {
  workflow?: string; // JSON string of workflow data
}

// Simple debounce utility
function debounce<T extends (...args: any[]) => any>(func: T, delay: number): T {
  let timeout: ReturnType<typeof setTimeout>;
  return ((...args: Parameters<T>) => {
    clearTimeout(timeout);
    timeout = setTimeout(() => func(...args), delay);
  }) as T;
}

const initialNodes = [
  { id: '1', position: { x: 0, y: 0 }, data: { label: 'Hello' } },
  { id: '2', position: { x: 100, y: 100 }, data: { label: 'World' } },
];
const initialEdges = [{ id: 'e1-2', source: '1', target: '2' }];

function FlowEditor({ workflow, setSelectedNode }: AppProps & { setSelectedNode: (node: Node | null) => void; }) {
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

  // Effect to load workflow data from prop
  useEffect(() => {
    if (workflow) {
      try {
        const parsedWorkflow = JSON.parse(workflow);
        setNodes(parsedWorkflow.nodes || []);
        setEdges(parsedWorkflow.edges || []);
      } catch (e) {
        console.error("Failed to parse workflow JSON from prop:", e);
      }
    } else {
      setNodes(initialNodes);
      setEdges(initialEdges);
    }
  }, [workflow, setNodes, setEdges]);

  const onConnect = useCallback((connection: any) => {
    setEdges((eds) => eds.concat(connection));
  }, [setEdges]);

  const onNodeClick = useCallback((_: React.MouseEvent, node: Node) => {
    setSelectedNode(node);
  }, [setSelectedNode]);

  const onPaneClick = useCallback(() => {
    setSelectedNode(null);
  }, [setSelectedNode]);

  // Debounced function to dispatch event
  const dispatchUpdateEvent = useCallback(debounce((currentNodes, currentEdges) => {
    const serializedWorkflowData = { nodes: currentNodes, edges: currentEdges }; // Simplified serialization
    window.dispatchEvent(new CustomEvent('workflowUpdated', {
      detail: {
        workflowId: JSON.parse(workflow || '{}').id, // Extract workflowId from prop
        workflowData: serializedWorkflowData
      },
      bubbles: true,
      composed: true,
    }));
    console.log('Dispatched workflowUpdated event', serializedWorkflowData);
  }, 500), [workflow]); // Recreate debounced function if workflow prop changes

  // Effect to dispatch update event on nodes/edges change
  useEffect(() => {
    dispatchUpdateEvent(nodes, edges);
  }, [nodes, edges, dispatchUpdateEvent]);

  const [{ isOver }, drop] = useDrop(() => ({
    accept: DRAGGABLE_NODE_TYPE,
    drop: (item: { type: string; label: string }, monitor) => {
      const clientOffset = monitor.getClientOffset();
      if (!reactFlowWrapper.current || !clientOffset) return;

      const reactFlowBounds = reactFlowWrapper.current.getBoundingClientRect();
      const position = project({
        x: clientOffset.x - reactFlowBounds.left,
        y: clientOffset.y - reactFlowBounds.top,
      });

      const newNode = {
        id: String(Date.now()), // Unique ID
        type: item.type,
        position,
        data: { label: item.label },
      };

      setNodes((nds) => nds.concat(newNode));
    },
    collect: (monitor) => ({
      isOver: monitor.isOver(),
    }),
  }), [project, setNodes]);

  return (
    <div className="reactflow-wrapper" ref={drop as unknown as React.Ref<HTMLDivElement>}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={onNodeClick}
        onPaneClick={onPaneClick}
        fitView
      >
        <Controls />
        <MiniMap />
        <Background variant={BackgroundVariant.Dots} gap={12} size={1} />
      </ReactFlow>
    </div>
  );
}

function AppWrapper(props: AppProps) {
  const [selectedNode, setSelectedNode] = useState<Node | null>(null);

  return (
    <DndProvider backend={HTML5Backend}>
      <div style={{ width: '100%', height: '100%', display: 'flex' }}>
        <NodeLibrary />
        <ReactFlowProvider>
          <FlowEditor {...props} setSelectedNode={setSelectedNode} />
        </ReactFlowProvider>
        <NodeInspector selectedNode={selectedNode} />
      </div>
    </DndProvider>
  );
}

export default AppWrapper;
