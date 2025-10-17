import { useQuery } from '@tanstack/react-query';
import { getPolicies } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { PolicyRecord } from '../../types/api';

export const PoliciesPage = () => {
  const { data: policies, isLoading } = useQuery({ queryKey: ['policies'], queryFn: getPolicies });

  const columns: ColumnDef<PolicyRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    { header: 'Max Clients', accessorKey: 'maxClients' },
    { header: 'Expiry', accessorKey: 'expiry' },
    { header: 'PIN', accessorKey: 'pin' },
  ];

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Registration Policies</h1>
      <Table columns={columns} data={policies ?? []} isLoading={isLoading} />
    </div>
  );
};
