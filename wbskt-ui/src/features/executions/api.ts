import apiClient from '../../lib/axios';
import type { ExecutionRecord } from '../../types/api';

export const getExecutions = async (): Promise<ExecutionRecord[]> => {
  console.warn('Using mock data for getExecutions');
  return Promise.resolve([
    {
      id: 101,
      workflowRefId: 'a1b2c3d4-e5f6-7890-1234-567890abcdef',
      status: 'Failed',
      triggeredAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
    },
    {
      id: 102,
      workflowRefId: 'b2c3d4e5-f6a7-8901-2345-67890abcdef1',
      status: 'Success',
      triggeredAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
    },
  ]);

  // Real implementation:
  // const { data } = await apiClient.get('/api/executions');
  // return data;
};
