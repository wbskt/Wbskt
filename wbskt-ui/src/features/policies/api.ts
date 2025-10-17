import apiClient from '../../lib/axios';
import type { PolicyRecord } from '../../types/api';

// Data Transfer Objects for write operations│
export type CreatePolicyData = Omit<PolicyRecord, 'id' | 'refId' | 'pin' | 'lastModified'>;
export type UpdatePolicyData = Omit<PolicyRecord, 'id' | 'refId' | 'pin' | 'lastModified'>;

export const getPolicies = async (): Promise<PolicyRecord[]> => {
  const { data } = await apiClient.get('/api/policies');
  return data;
};

export const createPolicy = async (data: CreatePolicyData): Promise<PolicyRecord> => {
  const { data: response } = await apiClient.post('/api/policies', data);
  return response;
};

export const updatePolicy = async ({ refId, data }: { refId: string; data: UpdatePolicyData }): Promise<PolicyRecord> => {
  const { data: response } = await apiClient.put(`/api/policies/${refId}`, data);
  return response;
};

export const deletePolicy = async (refId: string): Promise<void> => {
  await apiClient.delete(`/api/policies/${refId}`);
};
