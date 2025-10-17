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

// Step Configurations
export interface StepConfigurationBase {}

export interface TimedTriggerConfiguration extends StepConfigurationBase {
  cronExpression: string;
}

export interface SendEmailConfiguration extends StepConfigurationBase {
  integrationName: string;
  to: string;
  subject: string;
  body: string;
}

export interface IfConditionConfiguration extends StepConfigurationBase {
  leftOperand: string;
  operator: string;
  rightOperand: string;
}

export interface MakeHttpRequestConfiguration extends StepConfigurationBase {
  url: string;
  method: string;
  authentication?: string;
  headers?: string;
  body?: string;
}

export interface WorkflowStepExecutionRecord {
  id: number;
  workflowExecutionId: number;
  workflowStepId: number;
  status: 'Success' | 'Failed' | 'Skipped';
  startedAt: string;
  completedAt: string | null;
  inputContext: string | null;
  outputContext: string | null;
  errorLog: string | null;
}

export interface ExecutionDetailResponse {
  execution: ExecutionRecord;
  steps: WorkflowStepExecutionRecord[];
}