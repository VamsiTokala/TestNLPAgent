import { Component } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { NgFor, NgIf, NgClass, JsonPipe, DatePipe } from '@angular/common';
import { InsightAssistantApiService } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';

@Component({
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, NgClass, JsonPipe, DatePipe]
})
export class InsightAssistantComponent {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];

  readonly allClassifications = ['On Time', 'Delayed', 'Indeterminate'];

  response: AssistantQueryResponse | null = null;
  error: string | null = null;
  isLoading = false;
  form: FormGroup;

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {
    // Form must be initialised inside the constructor so that fb is available.
    // Class field initialisers run before constructor injection — using this.fb
    // outside the constructor causes TS2729 in strict mode.
    this.form = this.fb.group({ query: ['Find studies not completed on time'] });
  }

  runQuery(): void {
    this.error = null;
    this.response = null;
    this.isLoading = true;
    this.api.query(this.form.value.query || '').subscribe({
      next: (r: AssistantQueryResponse) => { this.response = r; this.isLoading = false; },
      error: (err) => {
        this.error = err?.error?.errors?.join(', ') ?? err?.error?.title ?? 'An unexpected error occurred. Please try again.';
        this.isLoading = false;
      }
    });
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }

  isClassificationIncluded(c: string): boolean {
    return this.response?.jsonPlan?.output?.includeClassifications?.includes(c) ?? false;
  }

  classificationBadgeClass(c: string): string {
    return c.toLowerCase().replace(' ', '-');
  }

  get allValidationPassed(): boolean {
    return this.response?.validation?.checks?.every(c => c.status === 'Passed') ?? false;
  }
}
