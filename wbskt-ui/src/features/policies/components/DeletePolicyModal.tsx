import { Modal } from '../../../components/common/Modal';
import { Button } from '../../../components/common/Button';

interface DeletePolicyModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  isDeleting: boolean;
}

export const DeletePolicyModal = ({ isOpen, onClose, onConfirm, isDeleting }: DeletePolicyModalProps) => {
  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Delete Policy">
      <p className="text-sm text-gray-600">Are you sure you want to delete this policy? This action cannot be undone.</p>
      <div className="mt-6 flex justify-end space-x-4">
        <Button variant="secondary" onClick={onClose} disabled={isDeleting}>Cancel</Button>
        <Button variant="destructive" onClick={onConfirm} disabled={isDeleting}>
          {isDeleting ? 'Deleting...' : 'Delete'}
        </Button>
      </div>
    </Modal>
  );
};
