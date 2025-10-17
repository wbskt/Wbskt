import { useQuery } from '@tanstack/react-query';
import { getDashboardStats } from './api';
import { StatsWidget } from './components/StatsWidget';
import { RecentExecutionsList } from './components/RecentExecutionsList';
import { Spinner } from '../../components/common/Spinner';

export const DashboardPage = () => {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['dashboardStats'],
    queryFn: getDashboardStats,
  });

  if (isLoading) {
    return (
      <div className="flex justify-center items-center h-full">
        <Spinner size="lg" />
      </div>
    );
  }

  if (isError) {
    return (
      <div className="text-red-500">
        Error fetching dashboard data: {error.message}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
        <StatsWidget title="Total Workflows" value={data!.totalWorkflows} />
        <StatsWidget title="Enabled Workflows" value={data!.enabledWorkflows} />
        <StatsWidget title="Executions (24h)" value={data!.totalExecutions24h} />
        <StatsWidget title="Failed (24h)" value={data!.failedExecutions24h} />
      </div>
      <div>
        <RecentExecutionsList executions={data!.recentExecutions} />
      </div>
    </div>
  );
};
