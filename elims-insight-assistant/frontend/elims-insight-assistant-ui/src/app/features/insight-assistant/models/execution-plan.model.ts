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
}

export interface PlanLimits {
  maxRows: number;
  pagination: boolean;
}

export interface ExecutionPlan {
  version: string;
  intent: string;
  entities: string[];
  operations: PlanOperation[];
  limits: PlanLimits;
}
