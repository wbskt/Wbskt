import type { WorkflowStepExecutionRecord } from '../../../types/api';
import { Button } from '../../../components/common/Button';
import clsx from 'clsx';

interface StepLogItemProps {
  step: WorkflowStepExecutionRecord;
  onViewData: (title: string, data: string | null) => void;
}

const StatusIcon = ({ status }: { status: string }) => {
  const styles = {
    Success: 'bg-green-500 text-white',
    Failed: 'bg-red-500 text-white',
    Skipped: 'bg-gray-400 text-white',
  };
  return (
    <div className={clsx('w-6 h-6 rounded-full flex items-center justify-center', styles[status as keyof typeof styles] ?? 'bg-gray-400')}>
      {status === 'Success' && '✓'}
      {status === 'Failed' && '✗'}
      {status === 'Skipped' && '-'}
    </div>
  );
};

export const StepLogItem = ({ step, onViewData }: StepLogItemProps) => {
  const duration = step.completedAt
    ? `${(new Date(step.completedAt).getTime() - new Date(step.startedAt).getTime())}ms`
    : '';

  return (
    <div className="p-4 border-b border-gray-200">
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          <StatusIcon status={step.status} />
          <div>
            <p className="font-medium">Step {step.workflowStepId}</p>
            <p className="text-xs text-gray-500">{duration}</p>
          </div>
        </div>
        <div className="space-x-2">
          <Button size="sm" variant="secondary" onClick={() => onViewData('Input Context', step.inputContext)}>View Input</Button>
          <Button size="sm" variant="secondary" onClick={() => onViewData('Output Context', step.outputContext)}>View Output</Button>
        </div>
      </div>
      {step.status === 'Failed' && (
        <div className="mt-2 p-3 bg-red-50 border border-red-200 rounded-md">
          <p className="text-sm text-red-700 font-mono">{step.errorLog}</p>
        </div>
      )}
    </div>
  );
};
