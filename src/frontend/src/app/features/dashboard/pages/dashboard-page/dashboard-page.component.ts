import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import { DashboardAlertDto, DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';

type DashboardQuickAction = Readonly<{
  labelKey: string;
  icon: string;
  route: string;
  testId: string;
}>;

type TranslatableText = Readonly<{
  key: string;
  params?: Record<string, unknown>;
}>;

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, ButtonModule, CardModule, DashboardKpiCardsComponent, ProgressSpinnerModule, TranslocoPipe],
  styleUrl: './dashboard-page.component.scss',
  template: `
    <section class="dashboard-page">
      <header>
        <h1>{{ 'dashboard.header.title' | transloco }}</h1>
        <p>{{ activeShopLabel().key | transloco: activeShopLabel().params }}</p>
      </header>

      @if (isLoading()) {
        <div class="latest-sales-panel__loading" aria-busy="true">
          <p-progressSpinner />
        </div>
      } @else {
        @if (errorMessage()) {
          <p class="dashboard-page__error">{{ errorMessage() | transloco }}</p>
        }

        @if (dashboard(); as dashboard) {
          <p>{{ dashboardStatus().key | transloco: dashboardStatus().params }}</p>

          <app-dashboard-kpi-cards [dashboard]="dashboard" />

          <div class="dashboard-kpis">
            <div class="kpi-card expenses-kpi">
              <span class="kpi-label">{{ 'dashboard.kpis.expenses' | transloco }}</span>
              <span class="kpi-value" data-testid="expenses-kpi-value">{{ formattedExpenses() }}</span>
            </div>

            <div class="kpi-card supplier-payables-kpi">
              <span class="kpi-label">{{ 'dashboard.kpis.supplierPayables' | transloco }}</span>
              <span class="kpi-value" data-testid="supplier-payables-kpi-value">{{ formattedSupplierPayables() }}</span>
            </div>
          </div>

          <div class="kpi-card stock-value-kpi" data-testid="stock-value-kpi">
            <span class="kpi-label">{{ 'dashboard.kpis.stockValue' | transloco }}</span>
            <span class="kpi-value">{{ formattedStockValue() }}</span>
          </div>

          <p-card class="dashboard-kpi-card">
            <ng-template pTemplate="header">
              <div class="dashboard-kpi-card__header">
                <p class="dashboard-kpi-card__eyebrow">{{ 'dashboard.kpis.customerAccounts' | transloco }}</p>
                <h2>{{ 'dashboard.kpis.customerCreditDue' | transloco }}</h2>
              </div>
            </ng-template>

            <div class="dashboard-kpi-card__value">
              <span>{{ dashboard.customerCreditDue | number: '1.2-2' }}</span>
            </div>
          </p-card>

          @if (pendingPurchaseOrderAlerts().length > 0) {
            <p-card class="alerts-panel">
              <ng-template pTemplate="header">
                <div class="alerts-panel__header">
                  <div>
                    <p class="alerts-panel__eyebrow">{{ 'dashboard.alerts.eyebrow' | transloco }}</p>
                    <h2>{{ 'dashboard.alerts.pendingPurchaseOrders' | transloco }}</h2>
                  </div>
                  <span class="alerts-panel__count">{{ pendingPurchaseOrderAlerts().length }}</span>
                </div>
              </ng-template>

              <ul class="alerts-list">
                @for (alert of pendingPurchaseOrderAlerts(); track alert.actionRoute + alert.message) {
                  <li class="alerts-list__item" data-testid="dashboard-alert-row">
                    <div class="alerts-list__content">
                      <span class="alerts-list__title">{{ alert.title }}</span>
                      <p class="alerts-list__message">{{ alert.message }}</p>
                    </div>
                    <button
                      type="button"
                      class="alerts-list__action"
                      data-testid="dashboard-alert-action"
                      (click)="openAlert(alert)"
                    >
                      {{ alert.actionLabel }}
                    </button>
                  </li>
                }
              </ul>
            </p-card>
          }

          @if (expiringBatchAlerts().length > 0) {
            <p-card class="alerts-panel">
              <ng-template pTemplate="header">
                <div class="alerts-panel__header">
                  <div>
                    <p class="alerts-panel__eyebrow">{{ 'dashboard.alerts.eyebrow' | transloco }}</p>
                    <h2>{{ 'dashboard.alerts.dashboardAlerts' | transloco }}</h2>
                  </div>
                  <span class="alerts-panel__count">{{ expiringBatchAlerts().length }}</span>
                </div>
              </ng-template>

              <ul class="alerts-list">
                @for (alert of expiringBatchAlerts(); track alert.actionRoute + alert.message) {
                  <li class="alerts-list__item" data-testid="dashboard-alert-row">
                    <div class="alerts-list__content">
                      <span class="alerts-list__title">{{ alert.title }}</span>
                      <p class="alerts-list__message">{{ alert.message }}</p>
                    </div>
                    <button
                      type="button"
                      class="alerts-list__action"
                      data-testid="dashboard-alert-action"
                      (click)="openAlert(alert)"
                    >
                      {{ alert.actionLabel }}
                    </button>
                  </li>
                }
              </ul>
            </p-card>
          }

          @if (lowStockAlerts().length > 0) {
            <p-card class="alerts-panel">
              <ng-template pTemplate="header">
                <div class="alerts-panel__header">
                  <div>
                    <p class="alerts-panel__eyebrow">{{ 'dashboard.alerts.needsAttention' | transloco }}</p>
                    <h2>{{ 'dashboard.alerts.lowStock' | transloco }}</h2>
                  </div>
                  <span class="alerts-panel__count">{{ lowStockAlerts().length }}</span>
                </div>
              </ng-template>

              <ul class="alerts-list">
                @for (alert of lowStockAlerts(); track alert.actionRoute + alert.message) {
                  <li class="alerts-list__item" data-testid="low-stock-alert-row">
                    <div class="alerts-list__content">
                      <span class="alerts-list__title">{{ alert.title }}</span>
                      <p class="alerts-list__message">{{ alert.message }}</p>
                    </div>
                    <button
                      type="button"
                      class="alerts-list__action"
                      data-testid="low-stock-alert-action"
                      (click)="openAlert(alert)"
                    >
                      {{ alert.actionLabel }}
                    </button>
                  </li>
                }
              </ul>
            </p-card>
          }

          <p-card class="latest-sales-panel">
            <ng-template pTemplate="header">
              <div class="latest-sales-panel__header">
                <div>
                  <p class="latest-sales-panel__eyebrow">{{ 'dashboard.latestSales.eyebrow' | transloco }}</p>
                  <h2>{{ 'dashboard.latestSales.title' | transloco }}</h2>
                </div>
                <span class="latest-sales-panel__count">{{ dashboard.latestSales.length }}</span>
              </div>
            </ng-template>

            @if (dashboard.latestSales.length === 0) {
              <div class="latest-sales-panel__empty">
                <p>{{ 'dashboard.latestSales.emptyTitle' | transloco }}</p>
                <span>{{ 'dashboard.latestSales.emptyDescription' | transloco }}</span>
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
        } @else if (!errorMessage()) {
          <p>{{ dashboardStatus().key | transloco: dashboardStatus().params }}</p>
        }

        <section class="dashboard-quick-actions" [attr.aria-label]="'dashboard.quickActions.ariaLabel' | transloco">
          @for (action of quickActions; track action.route) {
            <button
              pButton
              type="button"
              class="dashboard-quick-actions__button"
              severity="secondary"
              [label]="action.labelKey | transloco"
              [icon]="action.icon"
              [attr.data-testid]="action.testId"
              (click)="openQuickAction(action.route)"
            ></button>
          }
        </section>
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
  readonly pendingPurchaseOrderAlerts = computed(() =>
    this.dashboard()?.alerts.filter((alert) => alert.alertType === 'PendingPurchaseOrder') ?? [],
  );
  readonly expiringBatchAlerts = computed(() =>
    this.dashboard()?.alerts.filter((alert) => alert.alertType === 'ExpiringBatch') ?? [],
  );
  readonly lowStockAlerts = computed(() =>
    this.dashboard()?.alerts.filter((alert) => alert.alertType === 'LowStock') ?? [],
  );
  readonly quickActions: readonly DashboardQuickAction[] = [
    {
      labelKey: 'dashboard.quickActions.newSale',
      icon: 'pi pi-shopping-cart',
      route: '/sales/new',
      testId: 'dashboard-quick-action-sales-new',
    },
    {
      labelKey: 'dashboard.quickActions.batchStockEntry',
      icon: 'pi pi-plus',
      route: '/inventory/batch',
      testId: 'dashboard-quick-action-inventory-batch',
    },
    {
      labelKey: 'dashboard.quickActions.expenses',
      icon: 'pi pi-wallet',
      route: '/expenses',
      testId: 'dashboard-quick-action-expenses',
    },
    {
      labelKey: 'dashboard.quickActions.purchaseOrders',
      icon: 'pi pi-list',
      route: '/inventory/purchase-orders',
      testId: 'dashboard-quick-action-purchase-orders',
    },
    {
      labelKey: 'dashboard.quickActions.profitLoss',
      icon: 'pi pi-chart-line',
      route: '/sales/profit-loss',
      testId: 'dashboard-quick-action-profit-loss',
    },
  ];
  readonly activeShopLabel = computed<TranslatableText>(() => {
    const activeShop = this.permissions.activeShop();

    if (!activeShop) {
      return { key: 'dashboard.header.noActiveShop' };
    }

    return {
      key: 'dashboard.header.activeShop',
      params: {
        shopName: activeShop.shopName,
        role: activeShop.role,
      },
    };
  });
  readonly formattedExpenses = computed(() => {
    const amount = this.dashboard()?.netExpense ?? 0;
    return this.formatCurrency(amount);
  });

  readonly formattedSupplierPayables = computed(() => {
    const amount = this.dashboard()?.supplierPayables ?? 0;
    return this.formatCurrency(amount);
  });

  readonly dashboardStatus = computed<TranslatableText>(() => {
    const dashboard = this.dashboard();
    if (!dashboard) {
      return { key: 'dashboard.status.ready' };
    }

    return { key: 'dashboard.status.salesCount', params: { count: dashboard.salesCount } };
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
        this.errorMessage.set('dashboard.errors.loadFailed');
        this.isLoading.set(false);
      },
    });
  }

  openAlert(alert: DashboardAlertDto): void {
    void this.router.navigateByUrl(alert.actionRoute);
  }

  openQuickAction(route: string): void {
    void this.router.navigateByUrl(route);
  }

  private formatCurrency(amount: number): string {
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(amount);
  }
}
