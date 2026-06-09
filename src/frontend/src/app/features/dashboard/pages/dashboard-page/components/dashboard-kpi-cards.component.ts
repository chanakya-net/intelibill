import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';

import { DashboardDto } from '../../../services/dashboard.models';

@Component({
  selector: 'app-dashboard-kpi-cards',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe],
  template: `
    <div class="kpi-cards">
      <div class="kpi-card" data-testid="sales-revenue-card">
        <span class="kpi-label">Sales Revenue</span>
        <span class="kpi-value" data-testid="sales-revenue-value">
          {{ dashboard()?.salesRevenue | currency: 'INR' : 'symbol' : '1.2-2' }}
        </span>
      </div>
      <div class="kpi-card" data-testid="invoice-count-card">
        <span class="kpi-label">Invoice Count</span>
        <span class="kpi-value" data-testid="invoice-count-value">
          {{ dashboard()?.salesCount | number }}
        </span>
      </div>
    </div>
  `,
})
export class DashboardKpiCardsComponent {
  readonly dashboard = input<DashboardDto | null>(null);
}
