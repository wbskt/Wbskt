import type { ReactNode } from 'react';
import clsx from 'clsx';
import { Card } from '../../../components/common/Card';

interface StatsWidgetProps {
  title: string;
  value: number | string;
  icon?: ReactNode;
  variant?: 'default' | 'danger';
  trend?: string;
}

export const StatsWidget = ({ title, value, icon, variant = 'default', trend }: StatsWidgetProps) => {
  const borderColor = variant === 'danger' ? 'border-t-red-500' : 'border-t-blue-500';

  return (
    <Card className={clsx('p-5 border-t-4', borderColor)}>
      <div className="flex items-center">
        <div className="flex-shrink-0">
          {icon}
        </div>
        <div className="ml-5 w-0 flex-1">
          <dt className="text-sm font-medium text-gray-500 truncate">{title}</dt>
          <dd className="flex items-baseline">
            <p className="text-2xl font-semibold text-gray-900">{value}</p>
            {trend && (
              <p className={clsx('ml-2 flex items-baseline text-sm font-semibold', variant === 'danger' ? 'text-red-600' : 'text-green-600')}>
                {trend}
              </p>
            )}
          </dd>
        </div>
      </div>
    </Card>
  );
};
