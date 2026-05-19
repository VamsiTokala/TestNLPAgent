import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgFor, NgIf, JsonPipe, DatePipe } from '@angular/common';
import { timeout, TimeoutError } from 'rxjs';
import { InsightAssistantApiService, ProviderInfo } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';
import { ServiceContractEntry } from './models/service-contract.model';

@Component({
  standalone: true,
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, JsonPipe, DatePipe]
})
export class InsightAssistantComponent implements OnInit {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];

  response: AssistantQueryResponse | null = null;
  error: string | null = null;
  isLoading = false;
  slowWarning = false;
  form: FormGroup;

  providers: ProviderInfo[] = [];
  selectedProvider: string | null = null;

  contracts: ServiceContractEntry[] = [];
  showAddForm = false;
  addForm: FormGroup;
  addError: string | null = null;
  addSuccess = false;

  private _slowTimer: ReturnType<typeof setTimeout> | null = null;

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }
  get activeProviderName(): string { return this.providers.find(p => p.id === this.selectedProvider)?.name ?? ''; }
  get allValidationPassed(): boolean {
    return this.response?.validation?.checks?.every(c => c.status === 'Passed') ?? false;
  }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {
    this.form = this.fb.group({ query: ['Find studies not completed on time'] });
    this.addForm = this.fb.group({
      name:        ['', Validators.required],
      displayName: ['', Validators.required],
      action:      ['', Validators.required],
      fieldsRaw:   ['', Validators.required],
      purpose:     ['', Validators.required],
      description: [''],
      isRequired:  [false]
    });
  }

  ngOnInit(): void {
    this.api.getContracts().subscribe({ next: c => this.contracts = c });
    this.api.getProviders().subscribe({
      next: p => { this.providers = p; this.selectedProvider = p[0]?.id ?? null; }
    });
  }

  runQuery(): void {
    this.error = null;
    this.response = null;
    this.isLoading = true;
    this.slowWarning = false;
    if (this._slowTimer) clearTimeout(this._slowTimer);
    this._slowTimer = setTimeout(() => { if (this.isLoading) this.slowWarning = true; }, 5000);

    this.api.query(this.form.value.query || '', this.selectedProvider ?? undefined)
      .pipe(timeout(55000))
      .subscribe({
        next: (r: AssistantQueryResponse) => {
          this._clearSlow();
          this.response = r;
          this.isLoading = false;
        },
        error: (err) => {
          this._clearSlow();
          this.isLoading = false;
          if (err instanceof TimeoutError) {
            this.error = 'Request timed out after 55 s. Free-tier providers can be slow — try again or switch to Mock.';
          } else if (err?.status === 503) {
            this.error = err?.error?.message ?? 'AI provider temporarily unavailable — try again or switch to Mock.';
          } else if (err?.status === 400) {
            this.error = 'Validation failed: ' + (err?.error?.errors?.join(', ') ?? JSON.stringify(err?.error));
          } else {
            this.error = err?.error?.message ?? err?.error?.title ?? `Unexpected error (HTTP ${err?.status ?? '?'}).`;
          }
        }
      });
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }

  isServiceSelected(name: string): boolean {
    return this.response?.jsonPlan?.operations?.some(op => op.service === name) ?? false;
  }

  getServiceReason(name: string): string | null {
    return this.response?.jsonPlan?.operations?.find(op => op.service === name)?.reason ?? null;
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
      error: (err) => { this.addError = err?.error ?? 'Failed to register contract.'; }
    });
  }

  private _clearSlow(): void {
    if (this._slowTimer) { clearTimeout(this._slowTimer); this._slowTimer = null; }
    this.slowWarning = false;
  }
}
