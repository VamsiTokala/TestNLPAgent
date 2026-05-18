import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgFor, NgIf, NgClass, JsonPipe, DatePipe, SlicePipe, LowerCasePipe } from '@angular/common';
import { InsightAssistantApiService } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';
import { ServiceContractEntry } from './models/service-contract.model';

@Component({
  standalone: true,
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, NgClass, JsonPipe, DatePipe, SlicePipe, LowerCasePipe]
})
export class InsightAssistantComponent implements OnInit {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];

  readonly allClassifications = ['On Time', 'Delayed', 'Indeterminate'];

  // Query state
  response: AssistantQueryResponse | null = null;
  error: string | null = null;
  isLoading = false;
  form: FormGroup;

  // Service catalogue state
  contracts: ServiceContractEntry[] = [];
  showAddForm = false;
  addForm: FormGroup;
  addError: string | null = null;
  addSuccess = false;

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {
    this.form = this.fb.group({ query: ['Find studies not completed on time'] });
    this.addForm = this.fb.group({
      name:        ['', Validators.required],
      displayName: ['', Validators.required],
      action:      ['', Validators.required],
      fieldsRaw:   ['', Validators.required],   // comma-separated
      purpose:     ['', Validators.required],
      description: [''],
      isRequired:  [false]
    });
  }

  ngOnInit(): void {
    this.api.getContracts().subscribe({ next: c => this.contracts = c });
  }

  runQuery(): void {
    this.error = null;
    this.response = null;
    this.isLoading = true;
    this.api.query(this.form.value.query || '').subscribe({
      next: (r: AssistantQueryResponse) => { this.response = r; this.isLoading = false; },
      error: (err) => {
        this.error = err?.error?.errors?.join(', ') ?? err?.error?.title ?? 'An unexpected error occurred.';
        this.isLoading = false;
      }
    });
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }

  // ── Service catalogue helpers ──────────────────────────────────────────────

  isServiceSelected(name: string): boolean {
    return this.response?.jsonPlan?.operations?.some(op => op.service === name) ?? false;
  }

  getServiceReason(name: string): string | null {
    return this.response?.jsonPlan?.operations?.find(op => op.service === name)?.reason ?? null;
  }

  selectedCount(): number {
    return this.contracts.filter(c => this.isServiceSelected(c.name)).length;
  }

  submitAddForm(): void {
    if (this.addForm.invalid) return;
    this.addError = null;
    const v = this.addForm.value;
    const entry: ServiceContractEntry = {
      name:        v.name.trim(),
      displayName: v.displayName.trim(),
      action:      v.action.trim(),
      fields:      v.fieldsRaw.split(',').map((f: string) => f.trim()).filter(Boolean),
      purpose:     v.purpose.trim(),
      description: v.description?.trim() || v.purpose.trim(),
      isRequired:  v.isRequired
    };
    this.api.registerContract(entry).subscribe({
      next: (updated) => {
        this.contracts = updated;
        this.addSuccess = true;
        this.addForm.reset({ isRequired: false });
        setTimeout(() => { this.addSuccess = false; this.showAddForm = false; }, 1800);
      },
      error: (err) => {
        this.addError = err?.error ?? 'Failed to register contract.';
      }
    });
  }

  // ── AI panel helpers ───────────────────────────────────────────────────────

  isClassificationIncluded(c: string): boolean {
    return this.response?.jsonPlan?.output?.includeClassifications?.includes(c) ?? false;
  }

  get allValidationPassed(): boolean {
    return this.response?.validation?.checks?.every(c => c.status === 'Passed') ?? false;
  }
}
