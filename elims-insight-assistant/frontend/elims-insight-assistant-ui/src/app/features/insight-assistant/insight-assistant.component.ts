import { Component } from '@angular/core';
import { FormBuilder } from '@angular/forms';
import { InsightAssistantApiService } from './services/insight-assistant-api.service';

@Component({
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss']
})
export class InsightAssistantComponent {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];
  response: any;
  form = this.fb.group({ query: ['Find studies not completed on time'] });

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {}

  runQuery(): void {
    this.api.query(this.form.value.query || '').subscribe(r => this.response = r);
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }
}
