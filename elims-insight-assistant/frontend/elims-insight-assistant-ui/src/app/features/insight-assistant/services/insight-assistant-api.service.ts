import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AssistantQueryResponse } from '../models/assistant-query-response.model';

@Injectable({ providedIn: 'root' })
export class InsightAssistantApiService {
  constructor(private http: HttpClient) {}

  query(query: string): Observable<AssistantQueryResponse> {
    return this.http.post<AssistantQueryResponse>('/api/assistant/query', {
      query,
      userContext: {
        userId: 'demo-user',
        roles: ['StudyViewer', 'CoreLabsViewer'],
        legalEntities: ['EU', 'US']
      }
    });
  }
}
