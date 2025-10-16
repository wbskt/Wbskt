import type { HTMLAttributes } from 'react';
import clsx from 'clsx';

interface SpinnerProps extends HTMLAttributes<HTMLDivElement> {
  size?: 'sm' | 'md' | 'lg';
}

export const Spinner = ({ size = 'md', className, ...props }: SpinnerProps) => {
  const sizeStyles = {
    sm: 'h-4 w-4 border-2',
    md: 'h-8 w-8 border-4',
    lg: 'h-12 w-12 border-4',
  };

  return (
    <div
      className={clsx(
        'animate-spin rounded-full border-blue-500 border-t-transparent',
        sizeStyles[size],
        className
      )}
      style={{ borderTopColor: 'transparent' }} // Ensure the top border is transparent for the spin effect
      {...props}
    />
  );
};
