import { useCallback, useEffect } from 'react';
import ReactFlow, { Controls, Background, MiniMap, ReactFlowProvider, useNodesState, useEdgesState, BackgroundVariant } from 'reactflow';
import 'reactflow/dist/style.css';
import './index.scss';

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

function App({ workflow }: AppProps) {
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

  return (
    <div style={{ width: '100%', height: '100%' }}>
      <ReactFlowProvider>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          fitView
        >
          <Controls />
          <MiniMap />
          <Background variant={BackgroundVariant.Dots} gap={12} size={1} />
        </ReactFlow>
      </ReactFlowProvider>
    </div>
  );
}

export default App;