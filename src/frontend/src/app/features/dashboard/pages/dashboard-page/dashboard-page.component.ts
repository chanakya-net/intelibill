import { AsyncPipe, CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { DashboardService } from '../../services/dashboard.service';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, AsyncPipe, CurrencyPipe, DatePipe, CardModule, ProgressSpinnerModule],
  template: `
    <section class="dashboard-page">
      @if (dashboard$ | async; as dashboard) {
        <p-card class="latest-sales-panel">
          <ng-template pTemplate="header">
            <div class="latest-sales-panel__header">
              <div>
                <p class="latest-sales-panel__eyebrow">Recent Activity</p>
                <h2>Latest Sales</h2>
              </div>
              <span class="latest-sales-panel__count">{{ dashboard.latestSales.length }}</span>
            </div>
          </ng-template>

          @if (dashboard.latestSales.length === 0) {
            <div class="latest-sales-panel__empty">
              <p>No recent sales</p>
              <span>The latest active-shop sales will appear here.</span>
            </div>
          } @else {
            <ul class="latest-sales-list">
              @for (sale of dashboard.latestSales; track sale.saleId) {
                <li class="latest-sales-list__item">
                  <div class="latest-sales-list__primary">
                    <span class="latest-sales-list__invoice">{{ sale.invoiceNumber }}</span>
                    <span class="latest-sales-list__customer">{{ sale.customerDisplayName }}</span>
                  </div>
                  <div class="latest-sales-list__meta">
                    <span>{{ sale.soldAt | date:'short' }}</span>
                    <strong>{{ sale.totalAmount | number:'1.2-2' }}</strong>
                  </div>
                </li>
              }
            </ul>
          }
        </p-card>
      } @else {
        <div class="latest-sales-panel__loading" aria-busy="true">
          <p-progressSpinner />
        </div>
      }
    </section>
  `,
})
export class DashboardPageComponent {
  private readonly dashboardService = inject(DashboardService);

  readonly dashboard$ = this.dashboardService.getDashboard({});
}
