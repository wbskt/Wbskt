import { useState } from 'react';
import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { WorkflowCanvasWrapper } from './components/WorkflowCanvas';
import { NodesPanel } from './components/panels/NodesPanel';
import { ConfigPanel } from './components/panels/ConfigPanel';
import type { Node } from '@reactflow/core';
import type { StepConfigurationBase } from '../../types/api';

export const WorkflowBuilderPage = () => {
  const [selectedNode, setSelectedNode] = useState<Node | null>(null);
  // The nodes and setNodes state will be lifted into this component later
  // For now, this handler is a placeholder for the logic.
  const handleConfigurationChange = (newConfig: StepConfigurationBase) => {
    if (!selectedNode) return;

    console.log('Configuration changed for node:', selectedNode.id, newConfig);
    // Here, you would update the `nodes` array:
    // setNodes((nds) =>
    //   nds.map((node) => {
    //     if (node.id === selectedNode.id) {
    //       return { ...node, data: { ...node.data, configuration: newConfig } };
    //     }
    //     return node;
    //   })
    // );
  };

  return (
    <DndProvider backend={HTML5Backend}>
      <div className="flex h-full w-full bg-gray-200">
        <div className="w-64 bg-white border-r border-gray-300">
          <NodesPanel />
        </div>

        <div className="flex-grow">
          <WorkflowCanvasWrapper onNodeSelected={setSelectedNode} />
        </div>

        <div className="w-80 bg-white border-l border-gray-300">
          <ConfigPanel selectedNode={selectedNode} onConfigurationChange={handleConfigurationChange} />
        </div>
      </div>
    </DndProvider>
  );
};
