export interface WorkflowStep {
  id: string;
  workflowId: string;
  stepOrder: number;
  name: string;
  stepType: 'Trigger' | 'Action' | 'Modifier';
  stepIdentifier: string;
  stepConfiguration: string; // JSON string
  onSuccessStepId: string | null;
  onFailureStepId: string | null;
  PositionX: number;
  PositionY: number;
}

export interface Workflow {
  id: string;
  refId: string;
  name: string;
  description: string | null;
  isEnabled: boolean;
  triggerTypeId: number;
  triggerConfiguration: string | null; // JSON string
  lastModified: string;
  ViewportX: number | null;
  ViewportY: number | null;
  ViewportZoom: number | null;
  steps: WorkflowStep[];
}
