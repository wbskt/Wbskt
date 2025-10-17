import { Card } from "../../../components/common/Card";
import type { ExecutionRecord } from "../../../types/api";

interface ExecutionSummaryProps {
  execution: ExecutionRecord;
}

export const ExecutionSummary = ({ execution }: ExecutionSummaryProps) => {
  const duration = execution.completedAt
    ? `${(new Date(execution.completedAt).getTime() - new Date(execution.triggeredAt).getTime())}ms`
    : 'N/A';

  return (
    <Card className="p-4">
      <h3 className="text-lg font-medium text-gray-900 mb-4">Execution Summary</h3>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-sm">
        <div>
          <p className="font-medium text-gray-500">Status</p>
          <p className="mt-1">{execution.status}</p>
        </div>
        <div>
          <p className="font-medium text-gray-500">Triggered At</p>
          <p className="mt-1">{new Date(execution.triggeredAt).toLocaleString()}</p>
        </div>
        <div>
          <p className="font-medium text-gray-500">Duration</p>
          <p className="mt-1">{duration}</p>
        </div>
      </div>
    </Card>
  );
};
