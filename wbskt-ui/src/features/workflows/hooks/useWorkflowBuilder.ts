import type { Node } from '@reactflow/core';
import { addEdge, useEdgesState, useNodesState, useReactFlow, type Connection, type Edge, type EdgeChange, type NodeChange } from "@reactflow/core";
import { useState, useRef, useCallback } from "react";
import { useDrop } from "react-dnd";
import type { StepConfigurationBase } from "../../../types/api";
import { DRAGGABLE_NODE_TYPE } from "../components/panels/DraggableNode";

const initialNodes: Node[] = [];
const initialEdges: Edge[] = [];

export const useWorkflowBuilder = () => {
  const [nodes, setNodes, onNodesChangeInternal] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChangeInternal] = useEdgesState(initialEdges);
  const [selectedNode, setSelectedNode] = useState<Node | null>(null);
  const [isDirty, setIsDirty] = useState(false);

  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();

  const onNodesChange = (changes: NodeChange[]) => {
    onNodesChangeInternal(changes);
    setIsDirty(true);
  };

  const onEdgesChange = (changes: EdgeChange[]) => {
    onEdgesChangeInternal(changes);
    setIsDirty(true);
  };

  const onConnect = useCallback(
    (params: Connection) => {
      setEdges((eds) => addEdge(params, eds));
      setIsDirty(true);
    },
    [setEdges]
  );

  const onNodeClick = (_: React.MouseEvent, node: Node) => {
    setSelectedNode(node);
  };

  const onPaneClick = () => {
    setSelectedNode(null);
  };

  const handleConfigurationChange = (newConfig: StepConfigurationBase) => {
    if (!selectedNode) return;
    setNodes((nds) =>
      nds.map((node) => {
        if (node.id === selectedNode.id) {
          return { ...node, data: { ...node.data, configuration: newConfig } };
        }
        return node;
      })
    );
    setIsDirty(true);
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
          data: { label: item.label, stepIdentifier: item.stepIdentifier, configuration: {} },
        };

        setNodes((nds) => nds.concat(newNode));
        setIsDirty(true);
      }
    },
    collect: (monitor) => ({ isOver: !!monitor.isOver() }),
  });

  return {
    nodes,
    edges,
    selectedNode,
    isDirty,
    reactFlowWrapper,
    dropRef: drop,
    onNodesChange,
    onEdgesChange,
    onConnect,
    onNodeClick,
    onPaneClick,
    handleConfigurationChange,
  };
};
