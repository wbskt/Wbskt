export interface DashboardStats {
  totalWorkflows: number;
  enabledWorkflows: number;
  totalExecutions24h: number;
  failedExecutions24h: number;
}

export interface Workflow {
  id: string;
  name: string;
  isEnabled: boolean;
  lastModified: string;
}
