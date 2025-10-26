import { Link } from 'react-router-dom';
import { formatDistanceToNow } from 'date-fns';
import { Table, type ColumnDef } from '../../../components/common/Table';
import { StatusBadge } from '../../../components/common/StatusBadge';
import type { RecentExecution } from '../types';

interface RecentExecutionsListProps {
  executions: RecentExecution[];
}

export const RecentExecutionsList = ({ executions }: RecentExecutionsListProps) => {
  const columns: ColumnDef<RecentExecution>[] = [
    {
      header: 'Workflow',
      accessorKey: 'workflowName',
      cell: (row) => <p className="font-medium">{row.workflowName}</p>,
    },
    {
      header: 'Timestamp',
      accessorKey: 'timestamp',
      cell: (row) => (
        <span className="text-gray-600">
          {formatDistanceToNow(new Date(row.timestamp), { addSuffix: true })}
        </span>
      ),
    },
    {
      header: 'Status',
      accessorKey: 'status',
      cell: (row) => <StatusBadge status={row.status} />,
    },
    {
      header: '',
      accessorKey: 'executionId',
      cell: (row) => (
        <Link to={`/executions/${row.executionId}`} className="text-blue-600 hover:underline font-medium">
          View
        </Link>
      ),
    },
  ];

  return (
    <div>
      <h3 className="text-lg font-medium text-gray-900 mb-4 px-4 pt-4">Recent Executions</h3>
      <Table columns={columns} data={executions} getRowId={(row) => row.executionId} />
    </div>
  );
};

