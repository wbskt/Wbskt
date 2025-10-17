export interface WorkflowRecord {
  id: number;
  refId: string;
  userId: number;
  name: string;
  description: string | null;
  isEnabled: boolean;
  triggerTypeId: number;
  triggerConfiguration: string | null;
  lastModified: string;
}

export interface ExecutionRecord {
  id: number;
  workflowRefId: string;
  status: 'Success' | 'Failed' | 'Running';
  triggeredAt: string;
  completedAt: string | null;
}

export interface PolicyRecord {
  id: number;
  refId: string;
  name: string;
  maxClients: number | null;
  expiry: string | null;
  pin: string;
}

export interface ClientRecord {
  id: number;
  refId: string;
  name: string | null;
  active: boolean;
}

export interface CredentialRecord {
  id: number;
  name: string;
  integrationType: string;
  lastModified: string;
}