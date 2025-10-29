import React from 'react';
import { useDrag } from 'react-dnd';

export const DRAGGABLE_NODE_TYPE = 'workflowNode';

interface NodeLibraryItemProps {
  type: string;
  label: string;
}

const NodeLibraryItem: React.FC<NodeLibraryItemProps> = ({ type, label }) => {
  const [{ isDragging }, drag] = useDrag(() => ({
    type: DRAGGABLE_NODE_TYPE,
    item: { type, label },
    collect: (monitor) => ({
      isDragging: monitor.isDragging(),
    }),
  }));

  return (
    <div
      ref={drag as unknown as React.Ref<HTMLDivElement>}
      className="node-library-item"
      style={{ opacity: isDragging ? 0.5 : 1 }}
    >
      {label}
    </div>
  );
};

export const NodeLibrary: React.FC = () => {
  return (
    <div className="node-library">
      <h3>Nodes</h3>
      <NodeLibraryItem type="trigger" label="Trigger" />
      <NodeLibraryItem type="action" label="Action" />
      <NodeLibraryItem type="condition" label="Condition" />
    </div>
  );
};
