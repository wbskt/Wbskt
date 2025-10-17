import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getExecutionDetails } from './api';
import { Spinner } from '../../components/common/Spinner';
import { ExecutionSummary } from './components/ExecutionSummary';
import { StepLogItem } from './components/StepLogItem';
import { ContextViewerModal } from './components/ContextViewerModal';

export const ExecutionDetailPage = () => {
  const { executionId } = useParams<{ executionId: string }>();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [modalData, setModalData] = useState<{ title: string; data: string | null }>({ title: '', data: null });

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['executionDetails', executionId],
    queryFn: () => getExecutionDetails(Number(executionId)),
    enabled: !!executionId, // Only run the query if the executionId is present
  });

  const handleViewData = (title: string, data: string | null) => {
    setModalData({ title, data });
    setIsModalOpen(true);
  };

  if (isLoading) {
    return <div className="flex justify-center items-center h-full"><Spinner size="lg" /></div>;
  }

  if (isError) {
    return <div className="text-red-500">Error fetching execution details: {error.message}</div>;
  }

  if (!data) {
    return <div>Execution not found.</div>;
  }

  return (
    <div className="space-y-6">
      <ExecutionSummary execution={data.execution} />
      <div>
        <h3 className="text-lg font-medium text-gray-900 mb-2">Step-by-Step Log</h3>
        <div className="bg-white border rounded-lg shadow-sm">
          {data.steps.map((step) => (
            <StepLogItem key={step.id} step={step} onViewData={handleViewData} />
          ))}
        </div>
      </div>
      <ContextViewerModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={modalData.title}
        data={modalData.data}
      />
    </div>
  );
};
