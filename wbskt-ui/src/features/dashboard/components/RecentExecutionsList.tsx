import { Card } from '../../../components/common/Card';
import type { RecentExecution } from '../types';
import clsx from 'clsx';

interface RecentExecutionsListProps {
  executions: RecentExecution[];
}

const StatusBadge = ({ status }: { status: 'Success' | 'Failed' }) => (
  <span
    className={clsx(
      'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium',
      status === 'Success' ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
    )}
  >
    {status}
  </span>
);

export const RecentExecutionsList = ({ executions }: RecentExecutionsListProps) => {
  return (
    <Card className="p-4">
      <h3 className="text-lg font-medium text-gray-900 mb-4">Recent Executions</h3>
      <ul className="divide-y divide-gray-200">
        {executions.map((execution) => (
          <li key={execution.executionId} className="py-3 flex justify-between items-center">
            <div>
              <p className="text-sm font-medium text-gray-900">{execution.workflowName}</p>
              <p className="text-sm text-gray-500">{new Date(execution.timestamp).toLocaleString()}</p>
            </div>
            <StatusBadge status={execution.status} />
          </li>
        ))}
      </ul>
    </Card>
  );
};
