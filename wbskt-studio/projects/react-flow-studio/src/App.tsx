import React, { useState, useCallback, useEffect, useRef } from 'react';
import ReactFlow, { Controls, Background, MiniMap, ReactFlowProvider, useReactFlow, type Node as RFNode, BackgroundVariant } from 'reactflow';
import 'reactflow/dist/style.css';
import './index.scss';
import { DndProvider, useDrop } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { NodeLibrary, DRAGGABLE_NODE_TYPE } from './components/NodeLibrary';
import { NodeInspector } from './components/NodeInspector';
import { useWorkflowStore } from './store/workflowStore';
import { serializeWorkflow, deserializeWorkflow } from './utils/workflow-utils';
import type { Workflow } from './types';

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

function FlowEditor({ workflow }: AppProps) {
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();

  const { nodes, edges, setNodes, setEdges, onNodesChange, onEdgesChange, onConnect, setSelectedNode } = useWorkflowStore();

  // Effect to load workflow data from prop
  useEffect(() => {
    if (workflow) {
      try {
        const parsedWorkflow: Workflow = JSON.parse(workflow);
        const { nodes: deserializedNodes, edges: deserializedEdges } = deserializeWorkflow(parsedWorkflow);
        setNodes(deserializedNodes);
        setEdges(deserializedEdges);
      } catch (e) {
        console.error("Failed to parse workflow JSON from prop:", e);
      }
    } else {
      // Reset to initial state if workflow prop is cleared or not provided
      setNodes([]); // Or some default initial nodes
      setEdges([]); // Or some default initial edges
    }
  }, [workflow, setNodes, setEdges]);

  const onNodeClick = useCallback((_: React.MouseEvent, node: RFNode) => {
    setSelectedNode(node);
  }, [setSelectedNode]);

  const onPaneClick = useCallback(() => {
    setSelectedNode(null);
  }, [setSelectedNode]);

  // Debounced function to dispatch event
  const dispatchUpdateEvent = useCallback(debounce((currentNodes, currentEdges) => {
    const parsedWorkflow: Workflow = JSON.parse(workflow || '{}');
    const workflowId = parsedWorkflow.id; // Assuming 'id' is available on the Workflow object
    if (!workflowId) {
      console.warn("Cannot dispatch workflowUpdated event: workflowId is missing.");
      return;
    }

    const serializedWorkflowData = serializeWorkflow(workflowId, currentNodes, currentEdges); // Use the serializer

    window.dispatchEvent(new CustomEvent('workflowUpdated', {
      detail: {
        workflowId: workflowId,
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

  const [, drop] = useDrop(() => ({
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
        data: { label: item.label, configuration: {} }, // Initialize configuration
      };

  const newNodesArray = [...nodes, newNode];
    setNodes(newNodesArray);    },
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
  const { selectedNode, handleNodeConfigChange } = useWorkflowStore();

  return (
    <DndProvider backend={HTML5Backend}>
      <div style={{ width: '100%', height: '100%', display: 'flex' }}>
        <NodeLibrary />
        <ReactFlowProvider>
          <FlowEditor {...props} />
        </ReactFlowProvider>
        <NodeInspector selectedNode={selectedNode} onConfigChange={handleNodeConfigChange} />
      </div>
    </DndProvider>
  );
}

export default AppWrapper;
