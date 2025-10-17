import { WorkflowCanvasWrapper } from './components/WorkflowCanvas';

export const WorkflowBuilderPage = () => {
  return (
    <div className="flex h-full w-full bg-gray-200">
      {/* Column 1: Nodes Panel */}
      <div className="w-64 bg-white border-r border-gray-300">
        <div className="p-4 font-bold border-b">Nodes</div>
        <p className="p-4 text-sm text-gray-500">Nodes panel placeholder</p>
      </div>

      {/* Column 2: Canvas */}
      <div className="flex-grow">
        <WorkflowCanvasWrapper />
      </div>

      {/* Column 3: Config Panel */}
      <div className="w-80 bg-white border-l border-gray-300">
        <div className="p-4 font-bold border-b">Configuration</div>
        <p className="p-4 text-sm text-gray-500">Config panel placeholder</p>
      </div>
    </div>
  );
};
