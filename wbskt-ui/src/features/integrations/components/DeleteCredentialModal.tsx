import { Modal } from '../../../components/common/Modal';
import { Button } from '../../../components/common/Button';

interface DeleteCredentialModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  isDeleting: boolean;
}

export const DeleteCredentialModal = ({ isOpen, onClose, onConfirm, isDeleting }: DeleteCredentialModalProps) => {
  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Delete Credential">
      <p className="text-sm text-gray-600">Are you sure you want to delete this credential? This action cannot be undone.</p>
      <div className="mt-6 flex justify-end space-x-4">
        <Button variant="secondary" onClick={onClose} disabled={isDeleting}>Cancel</Button>
        <Button variant="destructive" onClick={onConfirm} disabled={isDeleting}>
          {isDeleting ? 'Deleting...' : 'Delete'}
        </Button>
      </div>
    </Modal>
  );
};
