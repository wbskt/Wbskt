import apiClient from '../../lib/axios';
import type { PolicyRecord } from '../../types/api';

export const getPolicies = async (): Promise<PolicyRecord[]> => {
  console.warn('Using mock data for getPolicies');
  return Promise.resolve([
    { id: 1, refId: 'p1', name: 'Default Policy', maxClients: 10, expiry: null, pin: '123456' },
  ]);
};
