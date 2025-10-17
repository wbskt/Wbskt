import apiClient from '../../lib/axios';
import type { CredentialRecord } from '../../types/api';

export const getCredentials = async (): Promise<CredentialRecord[]> => {
  console.warn('Using mock data for getCredentials');
  return Promise.resolve([
    { id: 1, name: 'My Work SendGrid', integrationType: 'SendGrid', lastModified: new Date().toISOString() },
  ]);
};
