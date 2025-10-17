import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getPolicies, createPolicy, updatePolicy, deletePolicy } from './api';
import { Table, type ColumnDef } from '../../components/common/Table';
import type { PolicyRecord } from '../../types/api';
import { Button } from '../../components/common/Button';
import { ManagePolicyModal } from './components/ManagePolicyModal';
import { DeletePolicyModal } from './components/DeletePolicyModal';
import type { PolicyFormData } from './components/PolicyForm';

export const PoliciesPage = () => {
  const queryClient = useQueryClient();
  const [manageModalOpen, setManageModalOpen] = useState(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [selectedPolicy, setSelectedPolicy] = useState<PolicyRecord | null>(null);

  const { data: policies, isLoading } = useQuery({ queryKey: ['policies'], queryFn: getPolicies });

  const createMutation = useMutation({
    mutationFn: createPolicy,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['policies'] });
      setManageModalOpen(false);
    },
  });

  const updateMutation = useMutation({
    mutationFn: updatePolicy,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['policies'] });
      setManageModalOpen(false);
      setSelectedPolicy(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: deletePolicy,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['policies'] });
      setDeleteModalOpen(false);
      setSelectedPolicy(null);
    },
  });

  const handleManageSubmit = (data: PolicyFormData) => {
    if (selectedPolicy) {
      updateMutation.mutate({ refId: selectedPolicy.refId, data });
    } else {
      createMutation.mutate(data);
    }
  };

  const columns: ColumnDef<PolicyRecord>[] = [
    { header: 'Name', accessorKey: 'name' },
    { header: 'Max Clients', accessorKey: 'maxClients' },
    { header: 'Expiry', accessorKey: 'expiry', cell: (row) => row.expiry ? new Date(row.expiry).toLocaleString() : 'Never' },
    { header: 'PIN', accessorKey: 'pin' },
    {
      header: 'Actions',
      accessorKey: 'id',
      cell: (row) => (
        <div className="space-x-2">
          <Button size="sm" variant="secondary" onClick={() => { setSelectedPolicy(row); setManageModalOpen(true); }}>Edit</Button>
          <Button size="sm" variant="destructive" onClick={() => { setSelectedPolicy(row); setDeleteModalOpen(true); }}>Delete</Button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">Registration Policies</h1>
        <Button variant="primary" onClick={() => { setSelectedPolicy(null); setManageModalOpen(true); }}>Create Policy</Button>
      </div>
      <Table columns={columns} data={policies ?? []} isLoading={isLoading} />
      <ManagePolicyModal
        isOpen={manageModalOpen}
        onClose={() => setManageModalOpen(false)}
        onSubmit={handleManageSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        policyToEdit={selectedPolicy}
      />
      {selectedPolicy && (
        <DeletePolicyModal
          isOpen={deleteModalOpen}
          onClose={() => setDeleteModalOpen(false)}
          onConfirm={() => deleteMutation.mutate(selectedPolicy.refId)}
          isDeleting={deleteMutation.isPending}
        />
      )}
    </div>
  );
};