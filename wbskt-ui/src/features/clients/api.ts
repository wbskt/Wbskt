import apiClient from '../../lib/axios';
import type { ClientRecord } from '../../types/api';

export const getClients = async (): Promise<ClientRecord[]> => {
  console.warn('Using mock data for getClients');
  return Promise.resolve([
    { id: 1, refId: 'c1', name: 'Test Client 1', active: true },
  ]);
};
