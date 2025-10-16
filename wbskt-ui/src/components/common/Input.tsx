import type { InputHTMLAttributes } from 'react';
import clsx from 'clsx';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  error?: boolean;
}

export const Input = ({ error, className, ...props }: InputProps) => {
  const baseStyles = 'block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm placeholder-gray-400 focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm';
  const errorStyles = 'border-red-500 text-red-900 placeholder-red-300 focus:ring-red-500 focus:border-red-500';

  return (
    <input
      className={clsx(baseStyles, error && errorStyles, className)}
      {...props}
    />
  );
};
