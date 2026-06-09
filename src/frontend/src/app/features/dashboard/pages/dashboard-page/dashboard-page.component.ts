import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import { DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, CardModule, DashboardKpiCardsComponent, ProgressSpinnerModule],
  styleUrl: './dashboard-page.component.scss',
  template: `
    <section class="dashboard-page">
      <header>
        <h1>Dashboard</h1>
        <p>{{ activeShopLabel() }}</p>
      </header>

      @if (isLoading()) {
        <div class="latest-sales-panel__loading" aria-busy="true">
          <p-progressSpinner />
        </div>
      } @else {
        @if (errorMessage()) {
          <p class="dashboard-page__error">{{ errorMessage() }}</p>
        }

        @if (dashboard(); as dashboard) {
          <p>{{ dashboardStatus() }}</p>

          <app-dashboard-kpi-cards [dashboard]="dashboard" />

          <div class="kpi-card expenses-kpi">
            <span class="kpi-label">Expenses</span>
            <span class="kpi-value" data-testid="expenses-kpi-value">{{ formattedExpenses() }}</span>
          </div>

          <div class="kpi-card stock-value-kpi" data-testid="stock-value-kpi">
            <span class="kpi-label">Stock Value</span>
            <span class="kpi-value">{{ formattedStockValue() }}</span>
          </div>

          <p-card class="dashboard-kpi-card">
            <ng-template pTemplate="header">
              <div class="dashboard-kpi-card__header">
                <p class="dashboard-kpi-card__eyebrow">Customer Accounts</p>
                <h2>Customer Credit Due</h2>
              </div>
            </ng-template>

            <div class="dashboard-kpi-card__value">
              <span>{{ dashboard.customerCreditDue | number: '1.2-2' }}</span>
            </div>
          </p-card>

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
                      <span>{{ sale.soldAt | date: 'short' }}</span>
                      <strong>{{ sale.totalAmount | number: '1.2-2' }}</strong>
                    </div>
                  </li>
                }
              </ul>
            }
          </p-card>
        } @else {
          <p>{{ dashboardStatus() }}</p>
        }
      }
    </section>
  `,
})
export class DashboardPageComponent {
  private readonly dashboardService = inject(DashboardService);
  private readonly permissions = inject(ShopPermissionsService);
  private readonly router = inject(Router);

  readonly dashboard = signal<DashboardDto | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly activeShopLabel = computed(() => {
    const activeShop = this.permissions.activeShop();

    if (!activeShop) {
      return 'No active shop';
    }

    return `${activeShop.shopName} · ${activeShop.role}`;
  });
  readonly formattedExpenses = computed(() => {
    const amount = this.dashboard()?.netExpense ?? 0;
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(amount);
  });

  readonly dashboardStatus = computed(() => {
    if (this.errorMessage()) {
      return this.errorMessage();
    }

    const dashboard = this.dashboard();
    if (!dashboard) {
      return 'Dashboard ready.';
    }

    return `Sales count: ${dashboard.salesCount}`;
  });
  readonly formattedStockValue = computed(() => {
    const d = this.dashboard();
    const value = d?.stockValue ?? 0;
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value);
  });

  constructor() {
    if (!this.permissions.isOwnerOrManagerOfActiveShop()) {
      void this.router.navigateByUrl('/sales');
      return;
    }

    this.loadDashboard();
  }

  private loadDashboard(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.dashboardService.getDashboard({}).subscribe({
      next: (dashboard) => {
        this.dashboard.set(dashboard);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('dashboard.loadFailed');
        this.isLoading.set(false);
      },
    });
  }
}
