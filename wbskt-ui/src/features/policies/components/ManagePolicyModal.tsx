import { Modal } from '../../../components/common/Modal';
import type { PolicyRecord } from '../../../types/api';
import { PolicyForm, type PolicyFormData } from './PolicyForm';

interface ManagePolicyModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: PolicyFormData) => void;
  isSubmitting: boolean;
  policyToEdit?: PolicyRecord | null;
}

export const ManagePolicyModal = ({ isOpen, onClose, onSubmit, isSubmitting, policyToEdit }: ManagePolicyModalProps) => {
  const title = policyToEdit ? 'Edit Policy' : 'Create New Policy';

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={title}>
      <PolicyForm
        initialData={policyToEdit ?? undefined}
        onSubmit={onSubmit}
        isSubmitting={isSubmitting}
      />
    </Modal>
  );
};
