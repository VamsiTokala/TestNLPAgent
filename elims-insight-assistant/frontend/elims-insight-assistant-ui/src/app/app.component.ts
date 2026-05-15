import { Component } from '@angular/core';
import { InsightAssistantComponent } from './features/insight-assistant/insight-assistant.component';

@Component({
  selector: 'app-root',
  imports: [InsightAssistantComponent],
  template: '<app-insight-assistant></app-insight-assistant>'
})
export class AppComponent {}
