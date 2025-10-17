import apiClient from '../../lib/axios';
import type { WorkflowRecord } from '../../types/api'; // Assuming a global API types file
 // Assuming a global API types file

export const getWorkflows = async (): Promise<WorkflowRecord[]> => {
  // Using mock data until the backend endpoint is ready
  console.warn('Using mock data for getWorkflows');
  return Promise.resolve([
    {
      id: 1,
      refId: 'a1b2c3d4-e5f6-7890-1234-567890abcdef',
      userId: 1,
      name: 'Notify on High Temp',
      description: 'Sends an SMS when the warehouse temperature exceeds 40°C.',
      isEnabled: true,
      triggerTypeId: 4, // ClientData
      triggerConfiguration: '{ "clientId": "temp-sensor-01" }',
      lastModified: new Date().toISOString(),
    },
    {
      id: 2,
      refId: 'b2c3d4e5-f6a7-8901-2345-67890abcdef1',
      userId: 1,
      name: 'Daily Sales Report',
      description: 'Emails a summary of sales every morning at 8 AM.',
      isEnabled: false,
      triggerTypeId: 2, // Timed
      triggerConfiguration: '{ "cron": "0 8 * * *" }',
      lastModified: new Date().toISOString(),
    },
  ]);

  // Real implementation:
  // const { data } = await apiClient.get('/api/workflows');
  // return data;
};
