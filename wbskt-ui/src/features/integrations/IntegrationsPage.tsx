import { useQuery } from '@tanstack/react-query';
import { getCredentials } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { CredentialRecord } from '../../types/api';

export const IntegrationsPage = () => {
  const { data: credentials, isLoading } = useQuery({ queryKey: ['credentials'], queryFn: getCredentials });

  const columns: ColumnDef<CredentialRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    { header: 'Type', accessorKey: 'integrationType' },
    { header: 'Last Modified', accessorKey: 'lastModified', cell: (row) => new Date(row.lastModified).toLocaleDateString() },
  ];

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Integrations</h1>
      <Table columns={columns} data={credentials ?? []} isLoading={isLoading} />
    </div>
  );
};
