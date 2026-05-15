import { ExecutionPlan } from './execution-plan.model';
import { StudyCompletionResult } from './study-completion-result.model';

export interface ValidationCheck {
  name: string;
  status: string;
}

export interface ValidationResult {
  status: string;
  checks: ValidationCheck[];
  errors: string[];
}

export interface QuerySummary {
  onTime: number;
  delayed: number;
  indeterminate: number;
}

export interface AssistantQueryResponse {
  planId: string;
  traceId: string;
  status: string;
  markdownPlan: string;
  jsonPlan: ExecutionPlan;
  validation: ValidationResult;
  summary: QuerySummary;
  results: StudyCompletionResult[];
  message: string;
}
