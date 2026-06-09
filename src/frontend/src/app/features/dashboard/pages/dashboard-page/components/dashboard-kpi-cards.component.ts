import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { DashboardDto } from '../../../services/dashboard.models';

@Component({
  selector: 'app-dashboard-kpi-cards',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, TranslocoPipe],
  styleUrl: './dashboard-kpi-cards.component.scss',
  template: `
    <div class="kpi-cards">
      <div class="kpi-card" data-testid="sales-revenue-card">
        <span class="kpi-label">{{ 'dashboard.kpis.salesRevenue' | transloco }}</span>
        <span class="kpi-value" data-testid="sales-revenue-value">
          {{ dashboard()?.salesRevenue | currency: 'INR' : 'symbol' : '1.2-2' }}
        </span>
      </div>
      <div class="kpi-card" data-testid="invoice-count-card">
        <span class="kpi-label">{{ 'dashboard.kpis.invoiceCount' | transloco }}</span>
        <span class="kpi-value" data-testid="invoice-count-value">
          {{ dashboard()?.salesCount | number }}
        </span>
      </div>
      <div class="kpi-card" data-testid="net-profit-card">
        <span class="kpi-label">{{ 'dashboard.kpis.netProfit' | transloco }}</span>
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
            {{ 'dashboard.kpis.vsPreviousPeriod' | transloco }}
          </span>
        }
      </div>
      <div class="kpi-card" data-testid="low-stock-items-card">
        <span class="kpi-label">{{ 'dashboard.kpis.lowStockItems' | transloco }}</span>
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
