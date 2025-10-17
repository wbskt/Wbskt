import type { Handle, Position, NodeProps } from '@reactflow/core';
import type { ReactNode } from 'react';
import clsx from 'clsx';

interface BaseNodeProps {
  icon: ReactNode;
  label: string;
  borderColor?: string;
  children?: ReactNode;
}

export const BaseNode = ({ icon, label, borderColor = 'border-gray-400' }: BaseNodeProps) => {
  return (
    <div className={clsx('bg-white border-2 rounded-md shadow-lg w-60', borderColor)}>
      <div className="p-2 flex items-center border-b bg-gray-50 rounded-t-md">
        <div className="w-6 h-6 mr-2">{icon}</div>
        <div className="font-bold text-sm">{label}</div>
      </div>
      {/* Children can be used for optional content inside the node */}
      {/* <div className="p-2">{children}</div> */}
    </div>
  );
};
