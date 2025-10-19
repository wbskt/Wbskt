import { useState, useCallback, useRef, useEffect } from 'react';
import { useDrop } from 'react-dnd';
import {
  type Node,
  type Edge,
  useNodesState,
  useEdgesState,
  addEdge,
  type Connection,
  type NodeChange,
  type EdgeChange,
  useReactFlow,
} from '@reactflow/core';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useParams, useNavigate } from 'react-router-dom';
import { getWorkflow, createWorkflow, updateWorkflow, type CreateWorkflowData, type UpdateWorkflowData } from '../api';
import { DRAGGABLE_NODE_TYPE } from '../components/panels/DraggableNode';
import type { StepConfigurationBase, WorkflowStepRecord } from '../../../types/api';
import type { StepIdentifier } from '../../../common/enums/StepIdentifier';

const initialNodes: Node[] = [];
const initialEdges: Edge[] = [];

const deserializeWorkflow = (steps: WorkflowStepRecord[]) => {
    const nodes: Node[] = steps.map(step => ({
        id: step.id.toString(),
        type: step.stepType.toLowerCase() === 'modifier' ? 'condition' : (step.stepType.toLowerCase() === 'trigger' ? 'trigger' : 'action'),
        position: { x: step.PositionX ?? 0, y: step.PositionY ?? 0 },
        data: { label: step.name, stepIdentifier: step.stepIdentifier, configuration: JSON.parse(step.stepConfiguration ?? '{}') },
    }));

    const edges: Edge[] = [];
    steps.forEach(step => {
        if(step.onSuccessStepId) {
            edges.push({ id: `e${step.id}-${step.onSuccessStepId}`, source: step.id.toString(), target: step.onSuccessStepId.toString() });
        }
        if(step.onFailureStepId) {
            edges.push({ id: `e${step.id}-${step.onFailureStepId}-no`, source: step.id.toString(), target: step.onFailureStepId.toString(), sourceHandle: 'no' });
        }
    });

    return { nodes, edges };
}

export const useWorkflowBuilder = () => {
  const { refId } = useParams<{ refId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const isNewWorkflow = refId === 'new';

  const [nodes, setNodes, onNodesChangeInternal] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChangeInternal] = useEdgesState(initialEdges);
  const [selectedNode, setSelectedNode] = useState<Node | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [workflowName, setWorkflowName] = useState('Untitled Workflow');

  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const { project } = useReactFlow();

  const { data: loadedData, isLoading } = useQuery({
    queryKey: ['workflow', refId],
    queryFn: () => getWorkflow(refId!),
    enabled: !isNewWorkflow,
  });

  // Use useEffect to handle side-effects after data is successfully fetched
  useEffect(() => {
    if (loadedData) {
      const { nodes: loadedNodes, edges: loadedEdges } = deserializeWorkflow(loadedData.steps);
      setNodes(loadedNodes);
      setEdges(loadedEdges);
      setWorkflowName(loadedData.name);
      setIsDirty(false);
    }
  }, [loadedData, setNodes, setEdges]);

  const onNodesChange = (changes: NodeChange[]) => {
    onNodesChangeInternal(changes);
    setIsDirty(true);
  };

  const onEdgesChange = (changes: EdgeChange[]) => {
    onEdgesChangeInternal(changes);
    setIsDirty(true);
  };

  const onConnect = useCallback((params: Connection) => {
    setEdges((eds) => addEdge(params, eds));
    setIsDirty(true);
  }, [setEdges]);

  const onNodeClick = (_: React.MouseEvent, node: Node) => setSelectedNode(node);
  const onPaneClick = () => setSelectedNode(null);

  const handleConfigurationChange = (newConfig: StepConfigurationBase) => {
    if (!selectedNode) return;
    setNodes((nds) =>
      nds.map((node) =>
        node.id === selectedNode.id
          ? { ...node, data: { ...node.data, configuration: newConfig } }
          : node
      )
    );
    setIsDirty(true);
  };

  const [, drop] = useDrop({
    accept: DRAGGABLE_NODE_TYPE,
    drop: (item: { nodeType: string; label: string; stepIdentifier: StepIdentifier }, monitor) => {
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

  const createMutation = useMutation({ mutationFn: createWorkflow, onSuccess: (data) => {
    queryClient.invalidateQueries({ queryKey: ['workflows'] });
    setIsDirty(false);
    navigate(`/workflows/${data.refId}`, { replace: true });
  }});

  const updateMutation = useMutation({ mutationFn: updateWorkflow, onSuccess: (data) => {
    queryClient.invalidateQueries({ queryKey: ['workflows'] });
    queryClient.invalidateQueries({ queryKey: ['workflow', data.refId] });
    setIsDirty(false);
  }});

  const saveWorkflow = () => {
    const steps: Omit<WorkflowStepRecord, 'id' | 'workflowId'>[] = nodes.map((node, index) => ({
      stepOrder: index,
      name: node.data.label,
      stepType: node.type === 'condition' ? 'Modifier' : 'Action',
      stepIdentifier: node.data.stepIdentifier,
      stepConfiguration: JSON.stringify(node.data.configuration ?? {}),
      onSuccessStepId: edges.find(e => e.source === node.id)?.target ? parseInt(edges.find(e => e.source === node.id)!.target) : null,
      onFailureStepId: edges.find(e => e.source === node.id && e.sourceHandle === 'no')?.target ? parseInt(edges.find(e => e.source === node.id && e.sourceHandle === 'no')!.target) : null,
      PositionX: Math.round(node.position.x),
      PositionY: Math.round(node.position.y),
    }));

    const payload = {
      name: workflowName,
      description: loadedData?.description ?? '',
      isEnabled: loadedData?.isEnabled ?? true,
      triggerTypeId: loadedData?.triggerTypeId ?? 1,
      triggerConfiguration: loadedData?.triggerConfiguration ?? '{}',
      steps: steps as WorkflowStepRecord[], // Correctly cast the steps
    };

    if (isNewWorkflow) {
      createMutation.mutate(payload as unknown as CreateWorkflowData);
    } else {
      updateMutation.mutate({ refId: refId!, data: payload as unknown as UpdateWorkflowData });
    }
  };

  return {
    nodes, edges, selectedNode, isDirty, isLoading, reactFlowWrapper, dropRef: drop, workflowName, setWorkflowName,
    onNodesChange, onEdgesChange, onConnect, onNodeClick, onPaneClick, handleConfigurationChange, saveWorkflow,
  };
};
