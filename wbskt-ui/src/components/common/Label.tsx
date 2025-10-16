import type { LabelHTMLAttributes } from 'react';
import clsx from 'clsx';

export const Label = ({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) => {
  return (
    <label
      className={clsx('block text-sm font-medium text-gray-700', className)}
      {...props}
    />
  );
};
