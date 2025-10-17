import { useQuery } from '@tanstack/react-query';
import { getClients } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { ClientRecord } from '../../types/api';

export const ClientsPage = () => {
  const { data: clients, isLoading } = useQuery({ queryKey: ['clients'], queryFn: getClients });

  const columns: ColumnDef<ClientRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    { header: 'Active', accessorKey: 'active', cell: (row) => (row.active ? 'Yes' : 'No') },
  ];

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Registered Clients</h1>
      <Table columns={columns} data={clients ?? []} isLoading={isLoading} />
    </div>
  );
};
