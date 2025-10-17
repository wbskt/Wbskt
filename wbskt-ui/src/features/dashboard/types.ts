export interface RecentExecution {
  executionId: string;
  workflowName: string;
  status: 'Success' | 'Failed';
  timestamp: string;
}

export interface DashboardStats {
  totalWorkflows: number;
  enabledWorkflows: number;
  totalExecutions24h: number;
  failedExecutions24h: number;
  recentExecutions: RecentExecution[];
}
