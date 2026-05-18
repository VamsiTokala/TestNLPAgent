export interface PlanFilter {
  field: string;
  op: string;
  value: string | null;
}

export interface PlanOperation {
  service: string;
  action: string;
  select: string[];
  filters: PlanFilter[];
  reason: string | null;
}

export interface PlanLimits {
  maxRows: number;
  pagination: boolean;
}

export interface PlanOutput {
  includeClassifications: string[];
}

export interface ExecutionPlan {
  version: string;
  intent: string;
  entities: string[];
  operations: PlanOperation[];
  output: PlanOutput;
  limits: PlanLimits;
}
