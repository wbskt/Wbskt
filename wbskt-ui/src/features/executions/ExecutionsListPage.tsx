import { useQuery } from '@tanstack/react-query';
import { getExecutions } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { ExecutionRecord } from '../../types/api';
import { Link } from 'react-router-dom';

export const ExecutionsListPage = () => {
  const { data: executions, isLoading } = useQuery({
    queryKey: ['executions'],
    queryFn: getExecutions,
  });

  const columns: ColumnDef<ExecutionRecord>[] = [
    { header: 'Status', accessorKey: 'status' },
    { header: 'Workflow RefId', accessorKey: 'workflowRefId' },
    {
      header: 'Triggered At',
      accessorKey: 'triggeredAt',
      cell: (row) => new Date(row.triggeredAt).toLocaleString(),
    },
    {
        header: 'Details',
        accessorKey: 'id',
        cell: (row) => <Link to={`/executions/${row.id}`} className="text-blue-600 hover:underline">View</Link>,
    }
  ];

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Executions</h1>
      <Table
        columns={columns}
        data={executions ?? []}
        isLoading={isLoading}
        emptyState="No executions found."
        getRowId={(row) => row.id}
      />
    </div>
  );
};
