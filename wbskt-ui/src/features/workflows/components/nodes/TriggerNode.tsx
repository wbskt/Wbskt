import { Handle, Position, type NodeProps } from '@reactflow/core';
import { BaseNode } from './BaseNode';

// Placeholder Icon
const TriggerIcon = () => <svg xmlns="http://www.w3.org/2000/svg" className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" /></svg>;

export const TriggerNode = (props: NodeProps) => {
  return (
    <BaseNode icon={<TriggerIcon />} label={props.data.label} borderColor="border-green-500">
      <Handle type="source" position={Position.Bottom} />
    </BaseNode>
  );
};
