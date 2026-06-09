import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

import { DashboardService } from '../../services/dashboard.service';
import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [DashboardKpiCardsComponent],
  template: `<app-dashboard-kpi-cards [dashboard]="dashboard()" />`,
})
export class DashboardPageComponent {
  private readonly dashboardService = inject(DashboardService);
  readonly dashboard = toSignal(this.dashboardService.getDashboard({}), { initialValue: null });
}
