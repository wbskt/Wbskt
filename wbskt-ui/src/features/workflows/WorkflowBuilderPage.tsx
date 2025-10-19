import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { ReactFlowProvider } from '@reactflow/core';
import { WorkflowCanvas } from './components/WorkflowCanvas';
import { NodesPanel } from './components/panels/NodesPanel';
import { ConfigPanel } from './components/panels/ConfigPanel';
import { useWorkflowBuilder } from './hooks/useWorkflowBuilder';

// This inner component is necessary so that useWorkflowBuilder has access to the ReactFlowProvider context
const WorkflowBuilder = () => {
  const {
    nodes,
    edges,
    selectedNode,
    reactFlowWrapper,
    dropRef,
    onNodesChange,
    onEdgesChange,
    onConnect,
    onNodeClick,
    onPaneClick,
    handleConfigurationChange,
  } = useWorkflowBuilder();

  return (
    <div className="flex h-full w-full bg-gray-200">
      <div className="w-64 bg-white border-r border-gray-300">
        <NodesPanel />
      </div>

      <div className="flex-grow" ref={reactFlowWrapper}>
        <div ref={dropRef as unknown as React.Ref<HTMLDivElement>} style={{ height: '100%', width: '100%' }}>
          <WorkflowCanvas
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={onNodeClick}
            onPaneClick={onPaneClick}
          />
        </div>
      </div>

      <div className="w-80 bg-white border-l border-gray-300">
        <ConfigPanel selectedNode={selectedNode} onConfigurationChange={handleConfigurationChange} />
      </div>
    </div>
  );
};

export const WorkflowBuilderPage = () => {
  return (
    <DndProvider backend={HTML5Backend}>
      <ReactFlowProvider>
        <WorkflowBuilder />
      </ReactFlowProvider>
    </DndProvider>
  );
};