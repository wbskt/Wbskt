import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getCredentials, deleteCredential } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { CredentialRecord } from '../../types/api';
import { Button } from '../../components/common/Button';
import { SaveCredentialModal } from './components/SaveCredentialModal';
import { DeleteCredentialModal } from './components/DeleteCredentialModal';

export const IntegrationsPage = () => {
  const queryClient = useQueryClient();
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [selectedCredentialId, setSelectedCredentialId] = useState<number | null>(null);

  const { data: credentials, isLoading } = useQuery({ queryKey: ['credentials'], queryFn: getCredentials });

  const deleteMutation = useMutation({
    mutationFn: deleteCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['credentials'] });
      setDeleteModalOpen(false);
      setSelectedCredentialId(null);
    },
  });

  const columns: ColumnDef<CredentialRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    { header: 'Type', accessorKey: 'integrationType' },
    { header: 'Last Modified', accessorKey: 'lastModified', cell: (row) => new Date(row.lastModified).toLocaleDateString() },
    {
      header: 'Actions',
      accessorKey: 'id',
      cell: (row) => (
        <Button size="sm" variant="destructive" onClick={() => { setSelectedCredentialId(row.id); setDeleteModalOpen(true); }}>Delete</Button>
      ),
    },
  ];

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">Integrations</h1>
        <Button variant="primary" onClick={() => setSaveModalOpen(true)}>Add New Credential</Button>
      </div>
      <Table columns={columns} data={credentials ?? []} isLoading={isLoading} />
      <SaveCredentialModal isOpen={saveModalOpen} onClose={() => setSaveModalOpen(false)} />
      {selectedCredentialId && (
        <DeleteCredentialModal
          isOpen={deleteModalOpen}
          onClose={() => setDeleteModalOpen(false)}
          onConfirm={() => deleteMutation.mutate(selectedCredentialId)}
          isDeleting={deleteMutation.isPending}
        />
      )}
    </div>
  );
};