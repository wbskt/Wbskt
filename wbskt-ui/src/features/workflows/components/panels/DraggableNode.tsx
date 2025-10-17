import { useDrag } from 'react-dnd';

export const DRAGGABLE_NODE_TYPE = 'NODE';

interface DraggableNodeProps {
  nodeType: string;
  label: string;
  stepIdentifier: string;
}

export const DraggableNode = ({ nodeType, label, stepIdentifier }: DraggableNodeProps) => {
  const [{ isDragging }, drag] = useDrag(() => ({
    type: DRAGGABLE_NODE_TYPE,
    item: { nodeType, label, stepIdentifier },
    collect: (monitor) => ({
      isDragging: !!monitor.isDragging(),
    }),
  }));

  return (
    <div
      ref={drag as unknown as React.Ref<HTMLDivElement>}
      className="p-2 m-2 border border-gray-300 rounded-md bg-white cursor-grab"
      style={{ opacity: isDragging ? 0.5 : 1 }}
    >
      {label}
    </div>
  );
};
