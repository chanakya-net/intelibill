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
      <article class="kpi-card kpi-card--amber" data-testid="sales-revenue-card">
        <p class="kpi-label">{{ 'dashboard.kpis.salesRevenue' | transloco }}</p>
        <p class="kpi-value" data-testid="sales-revenue-value">
          {{ dashboard()?.salesRevenue | currency: 'INR' : 'symbol' : '1.0-0' }}
        </p>
      </article>

      <article class="kpi-card kpi-card--sage" data-testid="net-profit-card">
        <p class="kpi-label">{{ 'dashboard.kpis.netProfit' | transloco }}</p>
        <p class="kpi-value" data-testid="net-profit-value">
          {{ dashboard()?.netProfit | currency: 'INR' : 'symbol' : '1.0-0' }}
        </p>
        @if (netProfitChangePercent() !== null) {
          <p
            class="kpi-change"
            data-testid="net-profit-change-value"
            [class.kpi-change--positive]="netProfitChangePercent()! > 0"
            [class.kpi-change--negative]="netProfitChangePercent()! < 0"
          >
            {{ netProfitChangePercent()! > 0 ? '+' : '' }}{{ netProfitChangePercent() | number: '1.1-1' }}%
            {{ 'dashboard.kpis.vsPreviousPeriod' | transloco }}
          </p>
        }
      </article>

      <article class="kpi-card kpi-card--stone" data-testid="invoice-count-card">
        <p class="kpi-label">{{ 'dashboard.kpis.invoiceCount' | transloco }}</p>
        <p class="kpi-value" data-testid="invoice-count-value">
          {{ dashboard()?.salesCount | number }}
        </p>
      </article>

      <article class="kpi-card kpi-card--rose" data-testid="low-stock-items-card">
        <p class="kpi-label">{{ 'dashboard.kpis.lowStockItems' | transloco }}</p>
        <p class="kpi-value" data-testid="low-stock-items-value">
          {{ dashboard()?.lowStockItemCount | number }}
        </p>
      </article>

      <article class="kpi-card kpi-card--amber" data-testid="stock-value-kpi">
        <p class="kpi-label">{{ 'dashboard.kpis.stockValue' | transloco }}</p>
        <p class="kpi-value">{{ formattedStockValue() }}</p>
      </article>

      <article class="kpi-card kpi-card--amber">
        <p class="kpi-label">{{ 'dashboard.kpis.customerCreditDue' | transloco }}</p>
        <p class="kpi-value" data-testid="customer-credit-due-value">
          {{ dashboard()?.customerCreditDue | currency: 'INR' : 'symbol' : '1.0-0' }}
        </p>
      </article>

      <article class="kpi-card kpi-card--stone">
        <p class="kpi-label">{{ 'dashboard.kpis.supplierPayables' | transloco }}</p>
        <p class="kpi-value" data-testid="supplier-payables-kpi-value">{{ formattedSupplierPayables() }}</p>
      </article>

      <article class="kpi-card kpi-card--rose">
        <p class="kpi-label">{{ 'dashboard.kpis.expenses' | transloco }}</p>
        <p class="kpi-value" data-testid="expenses-kpi-value">{{ formattedExpenses() }}</p>
      </article>
    </div>
  `,
})
export class DashboardKpiCardsComponent {
  readonly dashboard = input<DashboardDto | null>(null);
  readonly netProfitChangePercent = computed(() => this.dashboard()?.netProfitChangePercent ?? null);

  readonly formattedExpenses = computed(() => this.formatCurrency(this.dashboard()?.netExpense ?? 0));
  readonly formattedSupplierPayables = computed(() => this.formatCurrency(this.dashboard()?.supplierPayables ?? 0));
  readonly formattedStockValue = computed(() => this.formatCurrency(this.dashboard()?.stockValue ?? 0));

  private formatCurrency(amount: number): string {
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(
      amount,
    );
  }
}
