import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';

import { DashboardDto } from '../../../services/dashboard.models';

@Component({
  selector: 'app-dashboard-kpi-cards',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe],
  styleUrl: './dashboard-kpi-cards.component.scss',
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
      <div class="kpi-card" data-testid="net-profit-card">
        <span class="kpi-label">Net Profit</span>
        <span class="kpi-value" data-testid="net-profit-value">
          {{ dashboard()?.netProfit | currency: 'INR' : 'symbol' : '1.2-2' }}
        </span>
        @if (netProfitChangePercent() !== null) {
          <span
            class="kpi-change"
            data-testid="net-profit-change-value"
            [class.kpi-change--positive]="netProfitChangePercent()! > 0"
            [class.kpi-change--negative]="netProfitChangePercent()! < 0"
          >
            {{ netProfitChangePercent()! > 0 ? '+' : '' }}{{ netProfitChangePercent() | number: '1.2-2' }}%
            vs previous period
          </span>
        }
      </div>
      <div class="kpi-card" data-testid="low-stock-items-card">
        <span class="kpi-label">Low Stock Items</span>
        <span class="kpi-value" data-testid="low-stock-items-value">
          {{ dashboard()?.lowStockItemCount | number }}
        </span>
      </div>
    </div>
  `,
})
export class DashboardKpiCardsComponent {
  readonly dashboard = input<DashboardDto | null>(null);
  readonly netProfitChangePercent = computed(() => this.dashboard()?.netProfitChangePercent ?? null);
}
