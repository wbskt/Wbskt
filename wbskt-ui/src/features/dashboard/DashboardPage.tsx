import { useQuery } from '@tanstack/react-query';
import { getDashboardStats } from './api';
import { StatsWidget } from './components/StatsWidget';
import { RecentExecutionsList } from './components/RecentExecutionsList';
import { Spinner } from '../../components/common/Spinner';
import { CogIcon, CheckCircleIcon, ArrowPathIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import { Card } from '../../components/common/Card';

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
      <div className="p-6 text-red-500">
        Error fetching dashboard data: {error.message}
      </div>
    );
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
      {/* Page Header */}
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
      </div>

      {/* KPI Card Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatsWidget
          title="Total Workflows"
          value={data!.totalWorkflows}
          icon={<CogIcon className="h-8 w-8 text-gray-400" />}
        />
        <StatsWidget
          title="Enabled Workflows"
          value={data!.enabledWorkflows}
          icon={<CheckCircleIcon className="h-8 w-8 text-gray-400" />}
        />
        <StatsWidget
          title="Executions (24h)"
          value={data!.totalExecutions24h}
          icon={<ArrowPathIcon className="h-8 w-8 text-gray-400" />}
        />
        <StatsWidget
          title="Failed (24h)"
          value={data!.failedExecutions24h}
          variant={data!.failedExecutions24h > 0 ? 'danger' : 'default'}
          icon={<ExclamationTriangleIcon className="h-8 w-8 text-gray-400" />}
        />
      </div>

      {/* Recent Executions Section */}
      <div className="mt-8">
        <Card>
          <RecentExecutionsList executions={data!.recentExecutions} />
        </Card>
      </div>
    </div>
  );
};
