import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class InsightAssistantApiService {
  constructor(private http: HttpClient) {}

  query(query: string) {
    return this.http.post('/api/assistant/query', {
      query,
      userContext: {
        userId: 'demo-user',
        roles: ['StudyViewer', 'CoreLabsViewer'],
        legalEntities: ['EU', 'US']
      }
    });
  }
}
