import clsx from 'clsx';
import {
  CheckCircleIcon,
  XCircleIcon,
  ArrowPathIcon,
} from '@heroicons/react/24/solid';

interface StatusBadgeProps {
  status: 'Success' | 'Failed' | 'Running' | string;
}

export const StatusBadge = ({ status }: StatusBadgeProps) => {
  const baseClasses = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium';

  const statusStyles: { [key: string]: { classes: string; icon: React.ElementType } } = {
    Success: {
      classes: 'bg-green-100 text-green-800',
      icon: CheckCircleIcon,
    },
    Failed: {
      classes: 'bg-red-100 text-red-800',
      icon: XCircleIcon,
    },
    Running: {
      classes: 'bg-blue-100 text-blue-800',
      icon: ArrowPathIcon,
    },
    default: {
      classes: 'bg-gray-100 text-gray-800',
      icon: CheckCircleIcon, // Placeholder
    },
  };

  const style = statusStyles[status] ?? statusStyles.default;
  const Icon = style.icon;

  return (
    <span className={clsx(baseClasses, style.classes)}>
      <Icon className={clsx('w-4 h-4 mr-1.5', status === 'Running' && 'animate-spin')} />
      {status}
    </span>
  );
};
