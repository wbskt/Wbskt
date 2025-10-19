import apiClient from '../../lib/axios';
import type { WorkflowDetailResponse, WorkflowRecord, WorkflowStepRecord } from '../../types/api';

// Data Transfer Objects for write operations
export type CreateWorkflowData = Omit<WorkflowDetailResponse, 'refId' | 'lastModified'>;
export type UpdateWorkflowData = Omit<WorkflowDetailResponse, 'refId' | 'lastModified'>;

export const getWorkflows = async (): Promise<WorkflowRecord[]> => {
  // Using mock data until the backend endpoint is ready
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
  ]);
};

export const getWorkflow = async (refId: string): Promise<WorkflowDetailResponse> => {
  console.warn(`Using mock data for getWorkflow for id: ${refId}`);
  const steps: WorkflowStepRecord[] = [
    { id: 1, workflowId: 1, stepOrder: 1, name: 'Timed Trigger', stepType: 'trigger', stepIdentifier: 'Timed', stepConfiguration: '{}', onSuccessStepId: 2, onFailureStepId: null, PositionX: 100, PositionY: 100 },
    { id: 2, workflowId: 1, stepOrder: 2, name: 'Send Email', stepType: 'action', stepIdentifier: 'ActionSendEmail', stepConfiguration: '{}', onSuccessStepId: null, onFailureStepId: null, PositionX: 400, PositionY: 100 },
  ];
  return Promise.resolve({
    refId: refId,
    name: 'Loaded Workflow',
    description: 'This was loaded from the API.',
    isEnabled: true,
    triggerTypeId: 2, // Timed
    triggerConfiguration: '{"cron":"0 0 * * *"}',
    lastModified: new Date().toISOString(),
    steps: steps,
  });
};

export const createWorkflow = async (data: CreateWorkflowData): Promise<WorkflowDetailResponse> => {
  console.warn('Mocking createWorkflow');
  const newRefId = crypto.randomUUID();
  return Promise.resolve({ ...data, refId: newRefId, lastModified: new Date().toISOString() });
  // const { data: response } = await apiClient.post('/api/workflows', data);
  // return response;
};

export const updateWorkflow = async ({ refId, data }: { refId: string; data: UpdateWorkflowData }): Promise<WorkflowDetailResponse> => {
  console.warn(`Mocking updateWorkflow for ${refId}`);
  return Promise.resolve({ ...data, refId, lastModified: new Date().toISOString() });
  // const { data: response } = await apiClient.put(`/api/workflows/${refId}`, data);
  // return response;
};