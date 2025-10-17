import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { WorkflowCanvasWrapper } from './components/WorkflowCanvas';
import { NodesPanel } from './components/panels/NodesPanel';

export const WorkflowBuilderPage = () => {
  return (
    <DndProvider backend={HTML5Backend}>
      <div className="flex h-full w-full bg-gray-200">
        <div className="w-64 bg-white border-r border-gray-300">
          <NodesPanel />
        </div>

        <div className="flex-grow">
          <WorkflowCanvasWrapper />
        </div>

        <div className="w-80 bg-white border-l border-gray-300">
          <div className="p-4 font-bold border-b">Configuration</div>
          <p className="p-4 text-sm text-gray-500">Select a node to configure it.</p>
        </div>
      </div>
    </DndProvider>
  );
};
