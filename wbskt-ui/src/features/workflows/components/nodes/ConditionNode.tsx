import { Handle, Position, type NodeProps } from '@reactflow/core';
import { BaseNode } from './BaseNode';

// Placeholder Icon
const ConditionIcon = () => <svg xmlns="http://www.w3.org/2000/svg" className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14 10l-2 2m0 0l-2-2m2 2V3l2 2m-2-2v5l-2 2" /></svg>;

export const ConditionNode = (props: NodeProps) => {
  return (
    <BaseNode icon={<ConditionIcon />} label={props.data.label} borderColor="border-yellow-500">
      <Handle type="target" position={Position.Top} />
      <Handle type="source" position={Position.Right} id="yes" style={{ top: '50%' }} />
      <Handle type="source" position={Position.Bottom} id="no" />
    </BaseNode>
  );
};
