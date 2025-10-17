import { Modal } from '../../../components/common/Modal';

interface ContextViewerModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  data: string | null;
}

export const ContextViewerModal = ({ isOpen, onClose, title, data }: ContextViewerModalProps) => {
  let formattedData = 'No data available.';
  if (data) {
    try {
      formattedData = JSON.stringify(JSON.parse(data), null, 2);
    } catch (e) {
      formattedData = 'Invalid JSON data.';
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={title}>
      <pre className="bg-gray-100 p-4 rounded-md text-sm overflow-x-auto">
        <code>{formattedData}</code>
      </pre>
    </Modal>
  );
};
