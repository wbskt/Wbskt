import type { HTMLAttributes } from 'react';
import clsx from 'clsx';

export const Card = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => {
  return (
    <div
      className={clsx('bg-white border rounded-lg shadow-sm', className)}
      {...props}
    />
  );
};