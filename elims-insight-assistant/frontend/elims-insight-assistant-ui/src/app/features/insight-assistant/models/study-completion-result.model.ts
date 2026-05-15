export interface StudyCompletionResult {
  studyId: string;
  studyCode: string;
  customer: string;
  plannedCompletionDate: string | null;
  actualCompletionDate: string | null;
  classification: 'On Time' | 'Delayed' | 'Indeterminate';
  reason: string;
  dataQualityFlags: string[];
}
