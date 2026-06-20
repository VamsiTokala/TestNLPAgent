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

export interface PlanAggregate {
  field: string;
  fn: string;
  as: string;
}

export interface PlanTransform {
  groupBy: string[];
  aggregates: PlanAggregate[];
}

export interface PlanSort {
  field: string;
  direction: string;
}

export interface ExecutionPlan {
  version: string;
  intent: string;
  entities: string[];
  operations: PlanOperation[];
  transform: PlanTransform;
  output: PlanOutput;
  sort: PlanSort[];
  limits: PlanLimits;
}
