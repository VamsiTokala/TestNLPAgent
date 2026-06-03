import { Component, NgZone, OnInit } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgFor, NgIf, JsonPipe } from '@angular/common';
import { timeout, TimeoutError } from 'rxjs';
import { InsightAssistantApiService, ProviderInfo } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';
import { ServiceContractEntry } from './models/service-contract.model';

const DEFAULT_PROVIDERS: ProviderInfo[] = [
  { id: 'mock', name: 'Mock (keyword matching)', available: true },
  { id: 'gemini', name: 'Gemini 2.5 Flash', available: false },
  { id: 'openrouter', name: 'OpenRouter', available: false }
];

@Component({
  standalone: true,
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, JsonPipe]
})
export class InsightAssistantComponent implements OnInit {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'List active protocols',
    'Show samples received this year',
    'Count all studies'
  ];

  response: AssistantQueryResponse | null = null;
  error: string | null = null;
  isLoading = false;
  slowWarning = false;
  form: FormGroup;

  providers: ProviderInfo[] = [...DEFAULT_PROVIDERS];
  selectedProvider: string | null = 'mock';

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
  get selectedServicesCount(): number {
    return new Set(this.response?.jsonPlan?.operations?.map(op => op.service) ?? []).size;
  }

  get datasetEntries(): Array<{ service: string; rows: Array<{ [field: string]: unknown }> }> {
    const datasets = this.response?.datasets ?? {};
    return Object.keys(datasets)
      .filter(k => datasets[k]?.length > 0)
      .map(service => ({ service, rows: datasets[service] }));
  }

  datasetColumns(rows: Array<{ [field: string]: unknown }>): string[] {
    return rows.length > 0 ? Object.keys(rows[0]) : [];
  }

  // Columns to render in the main Results grid. Derived from the first row so
  // they reflect the primary entity the query targeted. We hide a few
  // metadata-only fields that don't make sense in a table.
  private static readonly HIDDEN_RESULT_COLS = new Set(['dataQualityFlags']);
  resultsColumns(): string[] {
    const rows = (this.response?.results ?? []) as Array<Record<string, unknown>>;
    if (rows.length === 0) return [];
    return Object.keys(rows[0]).filter(c => !InsightAssistantComponent.HIDDEN_RESULT_COLS.has(c));
  }

  prettyHeader(col: string): string {
    // camelCase / snake_case → "Title Case"
    return col
      .replace(/[_-]+/g, ' ')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/\b\w/g, c => c.toUpperCase());
  }

  isClassificationColumn(col: string): boolean { return col === 'classification'; }
  isDateColumn(col: string): boolean {
    return /date|at$/i.test(col);
  }

  // True only for timeliness queries — i.e. when the engine produced a
  // `classification` column. Used to hide the On Time / Delayed / Indeterminate
  // summary cards for plain count / list / filter queries.
  hasClassifications(): boolean {
    return this.resultsColumns().some(c => this.isClassificationColumn(c));
  }

  formatCell(value: unknown): string {
    if (value === null || value === undefined) return '—';
    if (value instanceof Date) return value.toISOString();
    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) {
      const d = new Date(value);
      return isNaN(d.getTime()) ? value : d.toISOString().slice(0, 10);
    }
    if (Array.isArray(value)) return value.join(', ');
    return String(value);
  }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService, private zone: NgZone) {
    this.form = this.fb.group({ query: [''] });
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
    this.api.getContracts().subscribe({ next: c => this.zone.run(() => this.contracts = c) });
    this.api.getProviders().subscribe({
      next: p => this.zone.run(() => {
        this.providers = p?.length ? p : [...DEFAULT_PROVIDERS];
        this.selectedProvider = this.providers.find(x => x.available)?.id ?? this.providers[0]?.id ?? 'mock';
      }),
      error: () => this.zone.run(() => {
        this.providers = [...DEFAULT_PROVIDERS];
        this.selectedProvider = 'mock';
      })
    });
  }

  runQuery(): void {
    this.error = null;
    this.response = null;
    this.isLoading = true;
    this.slowWarning = false;
    if (this._slowTimer) clearTimeout(this._slowTimer);
    this._slowTimer = setTimeout(() => this.zone.run(() => { if (this.isLoading) this.slowWarning = true; }), 5000);

    this.api.query(this.form.value.query || '', this.selectedProvider ?? undefined)
      .pipe(timeout(250000))
      .subscribe({
        next: (r: AssistantQueryResponse) => {
          this.zone.run(() => {
            this._clearSlow();
            this.response = r;
            this.isLoading = false;
          });
        },
        error: (err) => {
          this.zone.run(() => {
            this._clearSlow();
            this.isLoading = false;
            if (err instanceof TimeoutError) {
              this.error = 'Request timed out after 250 s. Free-tier providers can be slow — try again or switch to Mock.';
            } else if (err?.status === 503) {
              this.error = err?.error?.message ?? 'AI provider temporarily unavailable — try again or switch to Mock.';
            } else if (err?.status === 400) {
              this.error = 'Validation failed: ' + (err?.error?.errors?.join(', ') ?? JSON.stringify(err?.error));
            } else {
              this.error = err?.error?.message ?? err?.error?.title ?? `Unexpected error (HTTP ${err?.status ?? '?'}).`;
            }
          });
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
        this.zone.run(() => {
          this.contracts = updated;
          this.addSuccess = true;
          this.addForm.reset({ isRequired: false });
          setTimeout(() => this.zone.run(() => { this.addSuccess = false; this.showAddForm = false; }), 1800);
        });
      },
      error: (err) => { this.zone.run(() => { this.addError = err?.error ?? 'Failed to register contract.'; }); }
    });
  }

  private _clearSlow(): void {
    if (this._slowTimer) { clearTimeout(this._slowTimer); this._slowTimer = null; }
    this.slowWarning = false;
  }
}
