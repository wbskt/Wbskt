import { useQuery } from '@tanstack/react-query';
import { getWorkflows } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { WorkflowRecord } from '../../types/api';
import { Link } from 'react-router-dom';
import { Button } from '../../components/common/Button';

const StatusBadge = ({ isEnabled }: { isEnabled: boolean }) => (
  <span
    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
      isEnabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'
    }`}
  >
    {isEnabled ? 'Enabled' : 'Disabled'}
  </span>
);

const TriggerTypeDisplay = ({ triggerTypeId }: { triggerTypeId: number }) => {
  const triggerTypes: { [key: number]: string } = {
    1: 'Manual',
    2: 'Timed',
    3: 'Webhook',
    4: 'ClientData',
  };
  return <span>{triggerTypes[triggerTypeId] ?? 'Unknown'}</span>;
};

export const WorkflowsListPage = () => {
  const { data: workflows, isLoading } = useQuery({
    queryKey: ['workflows'],
    queryFn: getWorkflows,
  });

  const columns: ColumnDef<WorkflowRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    {
      header: 'Status',
      accessorKey: 'isEnabled',
      cell: (row) => <StatusBadge isEnabled={row.isEnabled} />,
    },
    {
      header: 'Trigger',
      accessorKey: 'triggerTypeId',
      cell: (row) => <TriggerTypeDisplay triggerTypeId={row.triggerTypeId} />,
    },
    {
      header: 'Last Modified',
      accessorKey: 'lastModified',
      cell: (row) => new Date(row.lastModified).toLocaleDateString(),
    },
    {
      header: 'Actions',
      accessorKey: 'id', // Use a unique key
      cell: (row) => (
        <div className="space-x-2">
          <Link to={`/workflows/${row.refId}`}>
            <Button size="sm" variant="secondary">Edit</Button>
          </Link>
          <Button size="sm" variant="destructive">Delete</Button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">Workflows</h1>
        <Button variant="primary">Create Workflow</Button>
      </div>
      <Table
        columns={columns}
        data={workflows ?? []}
        isLoading={isLoading}
        emptyState="No workflows found."
        getRowId={(row) => row.id}
      />
    </div>
  );
};
