import apiClient from '../../lib/axios';
import type { CredentialRecord } from '../../types/api';

export type SaveCredentialData = Omit<CredentialRecord, 'id' | 'lastModified'>;

export const getCredentials = async (): Promise<CredentialRecord[]> => {
  const { data } = await apiClient.get('/api/credentials');
  return data;
};

export const saveCredential = async (data: SaveCredentialData): Promise<void> => {
  await apiClient.post('/api/credentials', data);
};

export const deleteCredential = async (id: number): Promise<void> => {
  await apiClient.delete(`/api/credentials/${id}`);
};
