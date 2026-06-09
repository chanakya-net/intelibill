import '@angular/compiler';

import { CurrencyPipe } from '@angular/common';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardPageComponent } from './dashboard-page.component';
import { OfflineSalesQueueSyncService } from '../../../sales/services/offline-sales-queue-sync.service';

describe('DashboardPageComponent', () => {
  const createSession = (role: 'Owner' | 'Manager' | 'Staff') =>
    ({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role, isDefault: true, lastUsedAt: null }],
    }) as never;

  const createDashboard = (overrides: Partial<DashboardDto> = {}): DashboardDto => ({
    generatedAt: '2026-06-09T10:30:00Z',
    startDate: '2026-06-01',
    endDate: '2026-06-09',
    salesCount: 0,
    salesRevenue: 0,
    hasNoSalesActivity: true,
    customerCreditDue: 0,
    salesBooked: 0,
    netSalesBooked: 0,
    wastageCost: 0,
    cashCollected: 0,
    profitBeforeTax: 0,
    profitAfterTax: 0,
    netProfit: 0,
    netProfitChangePercent: null,
    expenseRecorded: 0,
    expenseCorrection: 0,
    netExpense: 0,
    supplierPayables: 0,
    creditSalesAmount: 0,
    creditSalesPercentage: 0,
    paymentMix: null,
    creditShareWarning: false,
    runningLowStockCount: 0,
    criticalStockCount: 0,
    rankedShortageList: [],
    highestDueCustomer: null,
    topFiveDueCustomers: [],
    alerts: [],
    salesTrendSeries: [],
    profitTrendSeries: [],
    paymentMixTrendSeries: [],
    previousPeriodSummary: null,
    latestSales: [],
    lowStockItemCount: 0,
    stockValue: null,
    ...overrides,
  });

  const sessionSignal = signal(createSession('Owner'));

  const authService = {
    session: sessionSignal,
  };

  const dashboardService = {
    getDashboard: vi.fn(),
  };

  const offlineQueueCountsSignal = signal({
    pending: 0,
    syncing: 0,
    failed: 0,
    warning: 0,
    needsReview: 0,
    totalVisible: 0,
  });

  const offlineSalesQueueSync = {
    visibleCounts: offlineQueueCountsSignal,
    refreshActiveStatusCounts: vi.fn<OfflineSalesQueueSyncService['refreshActiveStatusCounts']>(async () =>
      offlineQueueCountsSignal(),
    ),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>().mockResolvedValue(true),
  };

  let fixture: ComponentFixture<DashboardPageComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    dashboardService.getDashboard.mockReset();
    router.navigateByUrl.mockClear();
    sessionSignal.set(createSession('Owner'));
    offlineQueueCountsSignal.set({
      pending: 0,
      syncing: 0,
      failed: 0,
      warning: 0,
      needsReview: 0,
      totalVisible: 0,
    });
    offlineSalesQueueSync.refreshActiveStatusCounts.mockClear();

    TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: DashboardService, useValue: dashboardService },
        { provide: Router, useValue: router },
        { provide: OfflineSalesQueueSyncService, useValue: offlineSalesQueueSync },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it.each(['Owner', 'Manager'] as const)('loads dashboard data for %s roles', (role) => {
    sessionSignal.set(createSession(role));
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          salesCount: 42,
          salesRevenue: 15000,
          hasNoSalesActivity: false,
          salesBooked: 15000,
          netSalesBooked: 14000,
          wastageCost: 500,
          cashCollected: 8000,
          profitBeforeTax: 2500,
          profitAfterTax: 2000,
          expenseRecorded: 1000,
          netExpense: 1000,
          supplierPayables: 2750,
          creditSalesAmount: 6000,
          creditSalesPercentage: 40,
          runningLowStockCount: 3,
          criticalStockCount: 1,
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(dashboardService.getDashboard).toHaveBeenCalledWith({});
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(fixture.componentInstance.dashboard()?.salesCount).toBe(42);
    expect(fixture.componentInstance.isLoading()).toBe(false);
  });

  it('renders sales revenue KPI from dashboard data', () => {
    const dashboard = createDashboard({ salesCount: 15, salesRevenue: 7500, hasNoSalesActivity: false });
    const expectedSalesRevenue = new CurrencyPipe('en-US').transform(
      dashboard.salesRevenue,
      'INR',
      'symbol',
      '1.2-2',
    );
    dashboardService.getDashboard.mockReturnValue(of(dashboard));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="sales-revenue-value"]');
    expect(valueEl?.textContent?.trim()).toBe(expectedSalesRevenue);
  });

  it('renders invoice count KPI from dashboard data', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(createDashboard({ salesCount: 15, salesRevenue: 7500, hasNoSalesActivity: false })),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="invoice-count-value"]');
    expect(valueEl?.textContent?.trim()).toContain('15');
  });

  it('renders net profit KPI from dashboard data', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(createDashboard({ netProfit: 2500, hasNoSalesActivity: false })),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const valueEl = fixture.nativeElement.querySelector('[data-testid="net-profit-value"]');
    expect(valueEl?.textContent?.trim()).toContain('₹');
    expect(valueEl?.textContent?.trim()).toContain('2,500');
  });

  it('renders expenses KPI card with formatted value when dashboard loaded', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ salesCount: 5, netExpense: 1250.75 })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const kpiValue = fixture.nativeElement.querySelector('[data-testid="expenses-kpi-value"]');
    expect(kpiValue).not.toBeNull();
    expect(kpiValue.textContent).toContain('₹');
    expect(fixture.componentInstance.formattedExpenses()).toContain('₹');
  });

  it('renders expenses KPI as ₹0 when netExpense is zero', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ netExpense: 0 })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.formattedExpenses()).toContain('₹');
    expect(fixture.componentInstance.formattedExpenses()).toContain('0');
  });

  it('renders supplier payables KPI card with formatted value when dashboard loaded', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(createDashboard({ salesCount: 5, hasNoSalesActivity: false, supplierPayables: 2750.5 })),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const kpiValue = fixture.nativeElement.querySelector('[data-testid="supplier-payables-kpi-value"]');
    expect(kpiValue).not.toBeNull();
    expect(kpiValue.textContent).toContain('₹');
    expect(fixture.componentInstance.formattedSupplierPayables()).toContain('₹');
  });

  it('renders supplier payables KPI as ₹0 when supplierPayables is zero', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ supplierPayables: 0 })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.formattedSupplierPayables()).toContain('₹');
    expect(fixture.componentInstance.formattedSupplierPayables()).toContain('0');
  });

  it('redirects staff users to /sales before loading dashboard data', () => {
    sessionSignal.set(createSession('Staff'));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/sales');
    expect(dashboardService.getDashboard).not.toHaveBeenCalled();
    expect(fixture.componentInstance.isLoading()).toBe(false);
    expect(fixture.componentInstance.dashboard()).toBeNull();
  });

  it('renders the empty recent activity state', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Latest Sales');
    expect(host.textContent).toContain('No recent sales');
  });

  it('renders the customer credit due KPI for zero balance', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ customerCreditDue: 0 })));

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Customer Credit Due');
    expect(host.textContent).toContain('0.00');
  });

  it('renders the customer credit due KPI for nonzero balance', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ customerCreditDue: 1250 })));

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('1,250.00');
  });

  it('renders populated recent activity entries', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          salesCount: 12,
          hasNoSalesActivity: false,
          latestSales: [
            {
              saleId: 'sale-1',
              invoiceNumber: 'INV-000123',
              customerDisplayName: 'Walk-in Customer',
              soldAt: '2026-06-09T10:00:00Z',
              totalAmount: 1250,
            },
            {
              saleId: 'sale-2',
              invoiceNumber: 'INV-000122',
              customerDisplayName: 'Asha',
              soldAt: '2026-06-09T09:15:00Z',
              totalAmount: 980,
            },
          ],
        }),
      ),
    );

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('INV-000123');
    expect(host.textContent).toContain('Walk-in Customer');
    expect(host.textContent).toContain('1,250.00');
    expect(host.textContent).toContain('Asha');
  });

  it('renders pending purchase order alerts with action button', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          alerts: [
            {
              alertType: 'PendingPurchaseOrder',
              priority: 0,
              title: 'Pending purchase order',
              message: 'PO-1001 from Acme Traders has been partially received.',
              actionLabel: 'Review purchase orders',
              actionRoute: '/inventory/purchase-orders',
            },
          ],
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Pending Purchase Orders');
    expect(host.textContent).toContain('PO-1001 from Acme Traders has been partially received.');
    expect(host.textContent).toContain('Review purchase orders');

    const actionButton = host.querySelector('[data-testid="dashboard-alert-action"]') as HTMLButtonElement | null;
    expect(actionButton).toBeTruthy();
    actionButton?.click();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/inventory/purchase-orders');
  });

  it('renders low stock alerts with action button', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          alerts: [
            {
              alertType: 'LowStock',
              priority: 2,
              title: 'Low stock',
              message: 'Almonds is running low (1 remaining, reorder at 10).',
              actionLabel: 'Manage inventory',
              actionRoute: '/inventory',
            },
          ],
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Low Stock');
    expect(host.textContent).toContain('Almonds is running low (1 remaining, reorder at 10).');
    expect(host.textContent).toContain('Manage inventory');

    const actionButton = host.querySelector('[data-testid="low-stock-alert-action"]') as HTMLButtonElement | null;
    expect(actionButton).toBeTruthy();
    actionButton?.click();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/inventory');
  });

  it('renders expiring batch alerts with action button', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          alerts: [
            {
              alertType: 'ExpiringBatch',
              priority: 1,
              title: 'Expiring batch',
              message: 'Rice batch B-002 expires on 12 Jun 2026.',
              actionLabel: 'Review batches',
              actionRoute: '/inventory/batches',
            },
          ],
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Dashboard Alerts');
    expect(host.textContent).toContain('Expiring batch');
    expect(host.textContent).toContain('Rice batch B-002 expires on 12 Jun 2026.');
    expect(host.textContent).toContain('Review batches');

    const actionButton = host.querySelector('[data-testid="dashboard-alert-action"]') as HTMLButtonElement | null;
    expect(actionButton).toBeTruthy();
    actionButton?.click();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/inventory/batches');
  });

  it.each([
    ['pending', { pending: 2, syncing: 0, failed: 0, warning: 0, needsReview: 0, totalVisible: 2 }],
    ['failed', { pending: 0, syncing: 0, failed: 1, warning: 0, needsReview: 0, totalVisible: 1 }],
    ['warning', { pending: 0, syncing: 0, failed: 0, warning: 3, needsReview: 0, totalVisible: 3 }],
    ['needs-review', { pending: 0, syncing: 0, failed: 0, warning: 0, needsReview: 4, totalVisible: 4 }],
  ] as const)('renders offline queue alert for %s counts', (_, counts) => {
    offlineQueueCountsSignal.set(counts);
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Offline sales queue');
    expect(host.textContent).toContain(`Pending ${counts.pending}`);
    expect(host.textContent).toContain(`Failed ${counts.failed}`);
    expect(host.textContent).toContain(`Warnings ${counts.warning}`);
    expect(host.textContent).toContain(`Needs review ${counts.needsReview}`);
    expect(host.textContent).toContain(`review ${counts.totalVisible} queued sale`);

    const actionButton = host.querySelector('[data-testid="offline-queue-alert-action"]') as HTMLButtonElement | null;
    expect(actionButton).toBeTruthy();
    actionButton?.click();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/sales');
  });

  it('renders badge count with actionable count only, excluding syncing, in mixed state', () => {
    offlineQueueCountsSignal.set({
      pending: 1,
      syncing: 2,
      failed: 0,
      warning: 0,
      needsReview: 0,
      totalVisible: 3,
    });
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Offline sales queue');
    const badgeEl = host.querySelector('.alerts-panel__count') as HTMLElement | null;
    expect(badgeEl?.textContent?.trim()).toBe('1');
    expect(host.textContent).toContain('review 1 queued sale');
  });

  it('does not render the offline queue alert for syncing-only counts', () => {
    offlineQueueCountsSignal.set({
      pending: 0,
      syncing: 3,
      failed: 0,
      warning: 0,
      needsReview: 0,
      totalVisible: 3,
    });
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('[data-testid="offline-queue-alert-row"]').length).toBe(0);
    expect(host.textContent).not.toContain('Offline sales queue');
  });

  it('does not render the offline queue alert when counts are zero', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('[data-testid="offline-queue-alert-row"]').length).toBe(0);
  });

  it('does not render low stock panel when no low stock alerts', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ alerts: [] })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const rows = host.querySelectorAll('[data-testid="low-stock-alert-row"]');
    expect(rows.length).toBe(0);
  });

  it('filters low stock alerts independently from purchase order alerts', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          alerts: [
            {
              alertType: 'PendingPurchaseOrder',
              priority: 1,
              title: 'Pending purchase order',
              message: 'PO-1001 is waiting.',
              actionLabel: 'Review purchase orders',
              actionRoute: '/inventory/purchase-orders',
            },
            {
              alertType: 'LowStock',
              priority: 2,
              title: 'Low stock',
              message: 'Rice is running low (2 remaining, reorder at 10).',
              actionLabel: 'Manage inventory',
              actionRoute: '/inventory',
            },
          ],
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.pendingPurchaseOrderAlerts().length).toBe(1);
    expect(fixture.componentInstance.lowStockAlerts().length).toBe(1);
    expect(fixture.componentInstance.lowStockAlerts()[0].alertType).toBe('LowStock');
  });

  it('filters expiring batch alerts independently from other alerts', () => {
    dashboardService.getDashboard.mockReturnValue(
      of(
        createDashboard({
          alerts: [
            {
              alertType: 'PendingPurchaseOrder',
              priority: 1,
              title: 'Pending purchase order',
              message: 'PO-1001 is waiting.',
              actionLabel: 'Review purchase orders',
              actionRoute: '/inventory/purchase-orders',
            },
            {
              alertType: 'ExpiringBatch',
              priority: 1,
              title: 'Expiring batch',
              message: 'Rice batch B-002 expires on 12 Jun 2026.',
              actionLabel: 'Review batches',
              actionRoute: '/inventory/batches',
            },
            {
              alertType: 'LowStock',
              priority: 2,
              title: 'Low stock',
              message: 'Rice is running low (2 remaining, reorder at 10).',
              actionLabel: 'Manage inventory',
              actionRoute: '/inventory',
            },
          ],
        }),
      ),
    );

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.pendingPurchaseOrderAlerts().length).toBe(1);
    expect(fixture.componentInstance.expiringBatchAlerts().length).toBe(1);
    expect(fixture.componentInstance.lowStockAlerts().length).toBe(1);
    expect(fixture.componentInstance.expiringBatchAlerts()[0].alertType).toBe('ExpiringBatch');
  });

  it('renders dashboard quick actions with labels and icons', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const actions = [
      { testId: 'dashboard-quick-action-sales-new', label: 'New Sale', icon: 'pi-shopping-cart' },
      { testId: 'dashboard-quick-action-inventory-batch', label: 'Batch Stock Entry', icon: 'pi-plus' },
      { testId: 'dashboard-quick-action-expenses', label: 'Expenses', icon: 'pi-wallet' },
      { testId: 'dashboard-quick-action-purchase-orders', label: 'Purchase Orders', icon: 'pi-list' },
      { testId: 'dashboard-quick-action-profit-loss', label: 'Profit & Loss', icon: 'pi-chart-line' },
    ] as const;

    for (const action of actions) {
      const button = fixture.nativeElement.querySelector(`[data-testid="${action.testId}"]`) as HTMLButtonElement | null;
      expect(button).toBeTruthy();
      expect(button?.textContent).toContain(action.label);
      expect(button?.querySelector(`.${action.icon}`)).toBeTruthy();
    }
  });

  it('navigates to the configured route when a quick action is clicked', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard()));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const actions = [
      { testId: 'dashboard-quick-action-sales-new', route: '/sales/new' },
      { testId: 'dashboard-quick-action-inventory-batch', route: '/inventory/batch' },
      { testId: 'dashboard-quick-action-expenses', route: '/expenses' },
      { testId: 'dashboard-quick-action-purchase-orders', route: '/inventory/purchase-orders' },
      { testId: 'dashboard-quick-action-profit-loss', route: '/sales/profit-loss' },
    ] as const;

    for (const action of actions) {
      const button = fixture.nativeElement.querySelector(`[data-testid="${action.testId}"]`) as HTMLButtonElement | null;
      expect(button).toBeTruthy();
      button?.click();
      expect(router.navigateByUrl).toHaveBeenCalledWith(action.route);
    }
  });

  it('renders stock value KPI card with formatted currency', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ stockValue: 24750.5 })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    const kpiCard: HTMLElement = fixture.nativeElement.querySelector('[data-testid="stock-value-kpi"]');
    expect(kpiCard).not.toBeNull();
    const kpiValue = kpiCard.querySelector('.kpi-value')?.textContent ?? '';
    expect(kpiValue).toContain('24,750');
  });

  it('formats stock value null as zero currency', () => {
    dashboardService.getDashboard.mockReturnValue(of(createDashboard({ stockValue: null })));

    fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.formattedStockValue()).toContain('0');
  });
});
