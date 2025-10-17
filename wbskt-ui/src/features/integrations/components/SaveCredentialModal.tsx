import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal } from '../../../components/common/Modal';
import { CredentialForm, type CredentialFormData } from './CredentialForm';
import { saveCredential } from '../api';

interface SaveCredentialModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const SaveCredentialModal = ({ isOpen, onClose }: SaveCredentialModalProps) => {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: saveCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['credentials'] });
      onClose();
    },
  });

  const handleSubmit = (data: CredentialFormData) => {
    mutation.mutate(data);
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Add New Credential">
      <CredentialForm onSubmit={handleSubmit} isSubmitting={mutation.isPending} />
    </Modal>
  );
};
