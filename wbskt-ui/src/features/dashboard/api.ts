import apiClient from '../../lib/axios';
import type { DashboardStats } from './types';

export const getDashboardStats = async (): Promise<DashboardStats> => {
  // This endpoint is a placeholder and will need to be created in the backend.
  // For now, we will return mock data.
  console.warn('Using mock data for getDashboardStats');
  return Promise.resolve({
    totalWorkflows: 27,
    enabledWorkflows: 22,
    totalExecutions24h: 312,
    failedExecutions24h: 14,
    recentExecutions: [
      { executionId: 'run_1', workflowName: 'Notify on High Temp', status: 'Failed', timestamp: new Date().toISOString() },
      { executionId: 'run_2', workflowName: 'Process Inventory', status: 'Success', timestamp: new Date().toISOString() },
      { executionId: 'run_3', workflowName: 'Daily Report', status: 'Success', timestamp: new Date().toISOString() },
    ],
  });

  // Real implementation:
  // const { data } = await apiClient.get('/api/dashboard/stats');
  // return data;
};
