import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DatePickerModule } from 'primeng/datepicker';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import { DashboardAlertDto, DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardChartComponent } from './components/dashboard-chart.component';
import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';
import { OfflineSalesQueueSyncService } from '../../../sales/services/offline-sales-queue-sync.service';
import type { OfflineSalesVisibleQueueCounts } from '../../../sales/utils/offline-sale-sync.mapper';

type DashboardQuickAction = Readonly<{
  labelKey: string;
  icon: string;
  route: string;
  testId: string;
}>;

type DashboardActionAlert = Readonly<{
  title: string;
  message: string;
  actionLabel: string;
  actionRoute: string;
  titleKey?: string;
  messageKey?: string;
  messageParams?: Record<string, unknown>;
  actionLabelKey?: string;
}>;

type DashboardPeriod = 'last7' | 'last30' | 'custom';

type DashboardDisplayAlert = Readonly<{
  id: string;
  badgeLabel?: string;
  badgeLabelKey?: string;
  badgeTone: 'orange' | 'pink' | 'blue' | 'sky';
  message: string;
  actionRoute: string;
  testId: string;
}>;

type TranslatableText = Readonly<{
  key: string;
  params?: Record<string, unknown>;
}>;

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    DatePickerModule,
    DashboardChartComponent,
    DashboardKpiCardsComponent,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  styleUrl: './dashboard-page.component.scss',
  template: `
    <section class="dashboard-page">
      <header class="dashboard-hero">
        <div class="dashboard-hero__intro">
          <h1>{{ greeting().key | transloco: greeting().params }}</h1>
          <p class="dashboard-hero__subtitle">{{ 'dashboard.header.subtitle' | transloco }}</p>
          @if (dashboard(); as dashboard) {
            <p class="dashboard-hero__period-pill">
              {{ formattedDateRange(dashboard.startDate, dashboard.endDate) }}
            </p>
          }
        </div>

        <div class="dashboard-hero__controls">
          <div
            class="period-toggle"
            role="group"
            [attr.aria-label]="'dashboard.ranges.ariaLabel' | transloco"
          >
            <button
              type="button"
              class="period-toggle__button"
              [class.is-active]="selectedPeriod() === 'last7'"
              (click)="setPeriod('last7')"
            >
              {{ 'dashboard.ranges.last7Days' | transloco }}
            </button>
            <button
              type="button"
              class="period-toggle__button"
              [class.is-active]="selectedPeriod() === 'last30'"
              (click)="setPeriod('last30')"
            >
              {{ 'dashboard.ranges.last30Days' | transloco }}
            </button>
            <button
              type="button"
              class="period-toggle__button"
              [class.is-active]="selectedPeriod() === 'custom'"
              (click)="setPeriod('custom')"
            >
              {{ 'dashboard.ranges.custom' | transloco }}
            </button>
          </div>

          @if (selectedPeriod() === 'custom') {
            <div class="dashboard-hero__custom-range">
              <div class="dashboard-hero__custom-field">
                <label class="dashboard-hero__custom-label" for="dashboard-from-date">
                  {{ 'dashboard.ranges.from' | transloco }}
                </label>
                <p-datepicker
                  ngSkipHydration
                  inputId="dashboard-from-date"
                  styleClass="dashboard-hero__date"
                  inputStyleClass="dashboard-hero__date-input"
                  [(ngModel)]="customFrom"
                  (ngModelChange)="onCustomRangeChanged()"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  iconDisplay="input"
                  appendTo="body"
                  [placeholder]="'dd/mm/yyyy'"
                  [maxDate]="customTo ?? undefined"
                  [disabled]="isLoading()"
                />
              </div>
              <div class="dashboard-hero__custom-field">
                <label class="dashboard-hero__custom-label" for="dashboard-to-date">
                  {{ 'dashboard.ranges.to' | transloco }}
                </label>
                <p-datepicker
                  ngSkipHydration
                  inputId="dashboard-to-date"
                  styleClass="dashboard-hero__date"
                  inputStyleClass="dashboard-hero__date-input"
                  [(ngModel)]="customTo"
                  (ngModelChange)="onCustomRangeChanged()"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  iconDisplay="input"
                  appendTo="body"
                  [placeholder]="'dd/mm/yyyy'"
                  [minDate]="customFrom ?? undefined"
                  [disabled]="isLoading()"
                />
              </div>
            </div>
          }
        </div>
      </header>

      @if (isLoading()) {
        <div class="dashboard-page__loading" aria-busy="true">
          <p-progressSpinner />
        </div>
      } @else {
        @if (errorMessage()) {
          <p class="dashboard-page__error">{{ errorMessage() | transloco }}</p>
        }

        @if (dashboard(); as dashboard) {
          <app-dashboard-kpi-cards [dashboard]="dashboard" />
          <app-dashboard-chart [dashboard]="dashboard" />

          <div class="dashboard-panels">
            <p-card class="latest-sales-panel">
              <ng-template pTemplate="header">
                <div class="latest-sales-panel__header">
                  <p class="latest-sales-panel__eyebrow">
                    {{ 'dashboard.latestSales.eyebrow' | transloco }}
                  </p>
                  <h2>{{ 'dashboard.latestSales.title' | transloco }}</h2>
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
                        <span class="latest-sales-list__customer">{{
                          sale.customerDisplayName
                        }}</span>
                      </div>
                      <div class="latest-sales-list__meta">
                        <strong>{{
                          sale.totalAmount | currency: 'INR' : 'symbol' : '1.0-0'
                        }}</strong>
                        <span>{{ sale.soldAt | date: 'dd MMM, h:mm a' }}</span>
                      </div>
                    </li>
                  }
                </ul>
              }

              <div class="latest-sales-panel__footer">
                <button
                  pButton
                  type="button"
                  class="latest-sales-panel__view-sales-action"
                  [outlined]="true"
                  severity="secondary"
                  icon="pi pi-arrow-right"
                  iconPos="right"
                  [label]="'dashboard.latestSales.viewSales' | transloco"
                  [attr.aria-label]="'dashboard.latestSales.viewSalesAria' | transloco"
                  (click)="openQuickAction('/sales')"
                ></button>
              </div>
            </p-card>

            <p-card class="alerts-panel">
              <ng-template pTemplate="header">
                <div class="alerts-panel__header">
                  <div>
                    <p class="alerts-panel__eyebrow">
                      {{ 'dashboard.alerts.eyebrow' | transloco }}
                    </p>
                    <h2>{{ 'dashboard.alerts.needsAttention' | transloco }}</h2>
                  </div>
                </div>
              </ng-template>

              @if (displayAlerts().length === 0) {
                <p class="alerts-panel__empty">{{ 'dashboard.alerts.empty' | transloco }}</p>
              } @else {
                <ul class="alerts-list">
                  @for (alert of displayAlerts(); track alert.id) {
                    <li class="alerts-list__item" [attr.data-testid]="alert.testId">
                      <span
                        class="alerts-list__badge"
                        [ngClass]="'alerts-list__badge--' + alert.badgeTone"
                      >
                        {{
                          alert.badgeLabelKey ? (alert.badgeLabelKey | transloco) : alert.badgeLabel
                        }}
                      </span>
                      <p class="alerts-list__message">{{ alert.message }}</p>
                      <button
                        type="button"
                        class="alerts-list__action"
                        (click)="openAlertRoute(alert.actionRoute)"
                      >
                        {{ 'dashboard.alerts.view' | transloco }}
                      </button>
                    </li>
                  }
                </ul>
              }
            </p-card>
          </div>
        } @else if (!errorMessage()) {
          <p class="dashboard-page__status">
            {{ dashboardStatus().key | transloco: dashboardStatus().params }}
          </p>
        }

        <section
          class="dashboard-quick-actions"
          [attr.aria-label]="'dashboard.quickActions.ariaLabel' | transloco"
        >
          @for (action of quickActions; track action.route) {
            <button
              pButton
              type="button"
              class="dashboard-quick-actions__button"
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
  private readonly offlineSalesQueueSync = inject(OfflineSalesQueueSyncService);

  readonly dashboard = signal<DashboardDto | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly selectedPeriod = signal<DashboardPeriod>('last30');
  customFrom: Date | null = null;
  customTo: Date | null = null;

  readonly pendingPurchaseOrderAlerts = computed(
    () =>
      this.dashboard()?.alerts.filter((alert) => alert.alertType === 'PendingPurchaseOrder') ?? [],
  );
  readonly expiringBatchAlerts = computed(
    () => this.dashboard()?.alerts.filter((alert) => alert.alertType === 'ExpiringBatch') ?? [],
  );
  readonly lowStockAlerts = computed(
    () => this.dashboard()?.alerts.filter((alert) => alert.alertType === 'LowStock') ?? [],
  );
  readonly offlineQueueCounts = this.offlineSalesQueueSync.visibleCounts;
  readonly offlineQueueActionableCount = computed(() =>
    this.getDisplayedOfflineQueueCount(this.offlineQueueCounts()),
  );
  readonly offlineQueueAlert = computed(() => {
    const counts = this.offlineQueueCounts();
    const visibleCount = this.getDisplayedOfflineQueueCount(counts);

    if (visibleCount === 0) {
      return null;
    }

    return {
      title: '',
      titleKey: 'dashboard.offlineQueue.title',
      message: '',
      messageKey:
        visibleCount === 1
          ? 'dashboard.offlineQueue.messageSingular'
          : 'dashboard.offlineQueue.messagePlural',
      messageParams: this.buildOfflineQueueMessageParams(counts, visibleCount),
      actionLabel: '',
      actionLabelKey: 'dashboard.offlineQueue.openSales',
      actionRoute: '/sales',
    } satisfies DashboardActionAlert;
  });
  readonly displayAlerts = computed((): DashboardDisplayAlert[] => {
    const alerts: DashboardDisplayAlert[] = [];

    for (const alert of this.lowStockAlerts()) {
      alerts.push(this.toDisplayAlert(alert, 'orange', 'low-stock-alert-row'));
    }

    for (const alert of this.expiringBatchAlerts()) {
      alerts.push(this.toDisplayAlert(alert, 'pink', 'dashboard-alert-row'));
    }

    for (const alert of this.pendingPurchaseOrderAlerts()) {
      alerts.push(this.toDisplayAlert(alert, 'blue', 'dashboard-alert-row'));
    }

    const offlineQueueAlert = this.offlineQueueAlert();
    if (offlineQueueAlert) {
      alerts.push({
        id: 'offline-queue',
        badgeLabelKey: offlineQueueAlert.titleKey,
        badgeTone: 'sky',
        message: this.formatOfflineQueueSummary(this.offlineQueueCounts()),
        actionRoute: offlineQueueAlert.actionRoute,
        testId: 'offline-queue-alert-row',
      });
    }

    return alerts;
  });
  readonly quickActions: readonly DashboardQuickAction[] = [
    {
      labelKey: 'dashboard.quickActions.newSale',
      icon: 'pi pi-shopping-cart',
      route: '/sales/new',
      testId: 'dashboard-quick-action-sales-new',
    },
    {
      labelKey: 'dashboard.quickActions.addInventory',
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
  readonly greeting = computed<TranslatableText>(() => {
    const activeShop = this.permissions.activeShop();
    const shopName = activeShop?.shopName ?? '';
    const hour = new Date().getHours();

    if (hour < 12) {
      return { key: 'dashboard.header.greetingMorning', params: { shopName } };
    }

    if (hour < 17) {
      return { key: 'dashboard.header.greetingAfternoon', params: { shopName } };
    }

    return { key: 'dashboard.header.greetingEvening', params: { shopName } };
  });
  readonly dashboardStatus = computed<TranslatableText>(() => {
    const dashboard = this.dashboard();
    if (!dashboard) {
      return { key: 'dashboard.status.ready' };
    }

    return { key: 'dashboard.status.salesCount', params: { count: dashboard.salesCount } };
  });

  constructor() {
    this.loadDashboard();
    void this.offlineSalesQueueSync.refreshActiveStatusCounts();
  }

  setPeriod(period: DashboardPeriod): void {
    this.selectedPeriod.set(period);

    if (period !== 'custom') {
      this.loadDashboard();
      return;
    }

    if (this.customFrom && this.customTo) {
      this.loadDashboard();
    }
  }

  onCustomRangeChanged(): void {
    if (this.selectedPeriod() !== 'custom' || !this.customFrom || !this.customTo) {
      return;
    }

    if (this.customFrom > this.customTo) {
      return;
    }

    this.loadDashboard();
  }

  formattedDateRange(startDate: string, endDate: string): string {
    const start = this.parseDateOnly(startDate);
    const end = this.parseDateOnly(endDate);

    if (!start || !end) {
      return `${startDate} – ${endDate}`;
    }

    const formatter = new Intl.DateTimeFormat('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
    return `${formatter.format(start)} – ${formatter.format(end)}`;
  }

  openAlert(alert: Pick<DashboardAlertDto, 'actionRoute'> | DashboardActionAlert): void {
    void this.router.navigateByUrl(alert.actionRoute);
  }

  openAlertRoute(route: string): void {
    void this.router.navigateByUrl(route);
  }

  openQuickAction(route: string): void {
    void this.router.navigateByUrl(route);
  }

  private loadDashboard(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.dashboardService.getDashboard(this.buildQueryParams()).subscribe({
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

  private buildQueryParams(): { from?: string; to?: string } {
    const period = this.selectedPeriod();
    const today = this.startOfDay(new Date());

    if (period === 'last7') {
      return {
        from: this.formatDateOnly(this.addDays(today, -6)),
        to: this.formatDateOnly(today),
      };
    }

    if (period === 'last30') {
      return {
        from: this.formatDateOnly(this.addDays(today, -29)),
        to: this.formatDateOnly(today),
      };
    }

    if (!this.customFrom || !this.customTo || this.customFrom > this.customTo) {
      return {};
    }

    return {
      from: this.formatDateOnly(this.startOfDay(this.customFrom)),
      to: this.formatDateOnly(this.startOfDay(this.customTo)),
    };
  }

  private toDisplayAlert(
    alert: DashboardAlertDto,
    badgeTone: DashboardDisplayAlert['badgeTone'],
    testId: string,
  ): DashboardDisplayAlert {
    return {
      id: `${alert.alertType}-${alert.actionRoute}-${alert.message}`,
      badgeLabel: alert.title,
      badgeTone,
      message: alert.message,
      actionRoute: alert.actionRoute,
      testId,
    };
  }

  private formatOfflineQueueSummary(counts: OfflineSalesVisibleQueueCounts): string {
    const parts: string[] = [];

    if (counts.pending > 0) {
      parts.push(`${counts.pending} pending`);
    }

    if (counts.failed > 0) {
      parts.push(`${counts.failed} failed sync`);
    }

    if (counts.warning > 0) {
      parts.push(`${counts.warning} warning`);
    }

    if (counts.needsReview > 0) {
      parts.push(`${counts.needsReview} needs review`);
    }

    return parts.join(', ');
  }

  private getDisplayedOfflineQueueCount(counts: OfflineSalesVisibleQueueCounts): number {
    return counts.pending + counts.failed + counts.warning + counts.needsReview;
  }

  private buildOfflineQueueMessageParams(
    counts: OfflineSalesVisibleQueueCounts,
    visibleCount: number,
  ): Record<string, unknown> {
    return {
      pending: counts.pending,
      failed: counts.failed,
      warning: counts.warning,
      needsReview: counts.needsReview,
      visibleCount,
    };
  }

  private parseDateOnly(value: string): Date | null {
    const parsed = new Date(`${value}T00:00:00`);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  private formatDateOnly(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private startOfDay(date: Date): Date {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
  }

  private addDays(date: Date, days: number): Date {
    const next = new Date(date);
    next.setDate(next.getDate() + days);
    return next;
  }
}
