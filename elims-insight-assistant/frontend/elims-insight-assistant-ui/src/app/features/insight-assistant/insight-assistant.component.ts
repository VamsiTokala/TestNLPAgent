import { Component } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { NgFor, NgIf, JsonPipe } from '@angular/common';
import { InsightAssistantApiService } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';

@Component({
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, JsonPipe]
})
export class InsightAssistantComponent {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];

  response: AssistantQueryResponse | null = null;
  form: FormGroup;

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {
    // Form must be initialised inside the constructor so that fb is available.
    // Class field initialisers run before constructor injection — using this.fb
    // outside the constructor causes TS2729 in strict mode.
    this.form = this.fb.group({ query: ['Find studies not completed on time'] });
  }

  runQuery(): void {
    this.api.query(this.form.value.query || '').subscribe(
      (r: AssistantQueryResponse) => { this.response = r; }
    );
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }
}
