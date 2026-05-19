import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgFor, NgIf, NgClass, JsonPipe, DatePipe, SlicePipe, LowerCasePipe } from '@angular/common';
import { timeout, TimeoutError } from 'rxjs';
import { InsightAssistantApiService, ProviderInfo } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';
import { ServiceContractEntry } from './models/service-contract.model';

@Component({
  standalone: true,
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, NgClass, JsonPipe, DatePipe, SlicePipe, LowerCasePipe]
})
export class InsightAssistantComponent implements OnInit, OnDestroy {
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

  // Provider selector
  providers: ProviderInfo[] = [];
  selectedProvider: string | null = null;

  // Service catalogue state
  contracts: ServiceContractEntry[] = [];
  showAddForm = false;
  addForm: FormGroup;
  addError: string | null = null;
  addSuccess = false;

  // Pipeline tracker: 0=idle 1=sending 2=generating 3=validating 4=executing 5=complete
  pipelineStep = 0;
  slowWarning = false;   // shown when step 2 runs > 15 s
  private _pipeTimers: ReturnType<typeof setTimeout>[] = [];

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }

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
      next: p => {
        this.providers = p;
        this.selectedProvider = p[0]?.id ?? null;
      }
    });
  }

  ngOnDestroy(): void { this.clearPipeTimers(); }

  runQuery(): void {
    this.error = null;
    this.response = null;
    this.isLoading = true;
    this.pipelineStep = 1;
    this.slowWarning = false;
    this.clearPipeTimers();

    const t1 = setTimeout(() => { if (this.isLoading) this.pipelineStep = 2; }, 700);
    const tSlow = setTimeout(() => { if (this.isLoading && this.pipelineStep === 2) this.slowWarning = true; }, 15000);
    this._pipeTimers.push(t1, tSlow);

    this.api.query(this.form.value.query || '', this.selectedProvider ?? undefined).pipe(timeout(55000)).subscribe({
      next: (r: AssistantQueryResponse) => {
        this.response = r;
        this.pipelineStep = 3;
        const t2 = setTimeout(() => {
          this.pipelineStep = 4;
          const t3 = setTimeout(() => {
            this.pipelineStep = 5;
            this.isLoading = false;
          }, 400);
          this._pipeTimers.push(t3);
        }, 400);
        this._pipeTimers.push(t2);
      },
      error: (err) => {
        this.clearPipeTimers();
        this.pipelineStep = 0;
        this.slowWarning = false;
        if (err instanceof TimeoutError) {
          this.error = 'Request timed out after 55 s. Free-tier providers can be slow — try again or switch to Mock.';
        } else if (err?.status === 500) {
          this.error = 'Server error — check the backend console for details. The AI response may have had an unexpected format.';
        } else {
          this.error = err?.error?.message ?? err?.error?.errors?.join(', ') ?? err?.error?.title ?? `Unexpected error (HTTP ${err?.status ?? '?'}).`;
        }
        this.isLoading = false;
      }
    });
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }

  // ── Pipeline helpers ───────────────────────────────────────────────────────────

  psClass(step: number): string {
    if (this.pipelineStep < step) return 'ps--pending';
    if (this.pipelineStep === step) return 'ps--active';
    return 'ps--done';
  }
  psIsDone(step: number): boolean   { return this.pipelineStep > step; }
  psIsActive(step: number): boolean { return this.pipelineStep === step; }

  classSlug(c: string): string { return c.toLowerCase().replace(/\s+/g, '-'); }

  private clearPipeTimers(): void {
    this._pipeTimers.forEach(t => clearTimeout(t));
    this._pipeTimers = [];
  }

  // ── Service catalogue helpers ──────────────────────────────────────────────────

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

  // ── AI panel helpers ───────────────────────────────────────────────────────────

  isClassificationIncluded(c: string): boolean {
    return this.response?.jsonPlan?.output?.includeClassifications?.includes(c) ?? false;
  }

  get allValidationPassed(): boolean {
    return this.response?.validation?.checks?.every(c => c.status === 'Passed') ?? false;
  }
}
