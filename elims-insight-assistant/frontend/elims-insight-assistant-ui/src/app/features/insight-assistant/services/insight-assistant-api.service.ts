import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AssistantQueryResponse } from '../models/assistant-query-response.model';
import { ServiceContractEntry } from '../models/service-contract.model';

export interface ProviderInfo { id: string; name: string; available: boolean; }

@Injectable({ providedIn: 'root' })
export class InsightAssistantApiService {
  constructor(private http: HttpClient) {}

  query(query: string, provider?: string): Observable<AssistantQueryResponse> {
    return this.http.post<AssistantQueryResponse>('/api/assistant/query', {
      query,
      provider: provider ?? null,
      userContext: {
        userId: 'demo-user',
        roles: ['StudyViewer', 'CoreLabsViewer'],
        legalEntities: ['EU', 'US']
      }
    });
  }

  getProviders(): Observable<ProviderInfo[]> {
    return this.http.get<ProviderInfo[]>('/api/assistant/providers');
  }

  getContracts(): Observable<ServiceContractEntry[]> {
    return this.http.get<ServiceContractEntry[]>('/api/assistant/contracts');
  }

  registerContract(entry: ServiceContractEntry): Observable<ServiceContractEntry[]> {
    return this.http.post<ServiceContractEntry[]>('/api/assistant/contracts', entry);
  }
}
