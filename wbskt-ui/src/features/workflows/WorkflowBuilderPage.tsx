import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { ReactFlowProvider } from '@reactflow/core';
import { WorkflowCanvas } from './components/WorkflowCanvas';
import { NodesPanel } from './components/panels/NodesPanel';
import { ConfigPanel } from './components/panels/ConfigPanel';
import { useWorkflowBuilder } from './hooks/useWorkflowBuilder';
import { Button } from '../../components/common/Button';
import { Spinner } from '../../components/common/Spinner';

// This inner component is necessary so that useWorkflowBuilder has access to the ReactFlowProvider context
const WorkflowBuilder = () => {
  const {
    nodes,
    edges,
    selectedNode,
    isDirty,
    isLoading,
    reactFlowWrapper,
    dropRef,
    workflowName,
    setWorkflowName,
    onNodesChange,
    onEdgesChange,
    onConnect,
    onNodeClick,
    onPaneClick,
    handleConfigurationChange,
    saveWorkflow,
  } = useWorkflowBuilder();

  if (isLoading) {
    return <div className="flex h-full w-full items-center justify-center"><Spinner size="lg" /></div>;
  }

  return (
    <div className="flex flex-col h-full w-full">
      <div className="p-2 border-b bg-white flex justify-between items-center">
        <input
          type="text"
          value={workflowName}
          onChange={(e) => setWorkflowName(e.target.value)}
          className="text-xl font-bold border-none focus:ring-0"
        />
        <Button onClick={saveWorkflow} disabled={!isDirty}>
          {isDirty ? 'Save Changes' : 'Saved'}
        </Button>
      </div>
      <div className="flex-grow flex min-h-0">
        <div className="w-64 bg-white border-r border-gray-300 overflow-y-auto">
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

        <div className="w-80 bg-white border-l border-gray-300 overflow-y-auto">
          <ConfigPanel selectedNode={selectedNode} onConfigurationChange={handleConfigurationChange} />
        </div>
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
