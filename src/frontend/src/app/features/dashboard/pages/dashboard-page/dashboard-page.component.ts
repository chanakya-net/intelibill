import { Component } from '@angular/core';

import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [DashboardKpiCardsComponent],
  template: `<app-dashboard-kpi-cards />`,
})
export class DashboardPageComponent {}
