import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { provideRouter } from '@angular/router';

import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderListItem,
  PurchaseOrderService,
} from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrdersListPageComponent } from './purchase-orders-list-page.component';

const purchaseOrderSignal = signal<readonly PurchaseOrderListItem[]>([
  {
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-000001',
    status: 'Draft',
    supplierName: 'Acme Traders',
    supplierReference: 'SUP-REF-001',
    lineCount: 2,
    expectedQuantity: 5,
    receivedQuantity: 3,
    expectedTotal: 1500,
    createdAt: '2026-06-01T00:00:00Z',
  },
]);

const loadingSignal = signal(false);
const detailLoadingSignal = signal(false);
const selectedOrderSignal = signal(null);
const createSucceededSignal = signal(false);
const errorMessageSignal = signal('');
const filtersSignal = signal(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
const paginationSignal = signal({ totalCount: 1, pageNumber: 1, pageSize: 20 });

const facade = {
  orders: purchaseOrderSignal,
  isLoadingList: loadingSignal,
  isLoadingDetail: detailLoadingSignal,
  selectedOrder: selectedOrderSignal,
  createSucceeded: createSucceededSignal,
  isSubmitting: signal(false),
  errorMessage: errorMessageSignal,
  filters: filtersSignal,
  pagination: paginationSignal,
  loadOrders: vi.fn(),
  loadDetail: vi.fn(),
  createDraft: vi.fn(),
  clearDetail: vi.fn(),
  clearError: vi.fn(),
  resetListFilters: vi.fn(),
};

describe('PurchaseOrdersListPageComponent', () => {
  beforeEach(() => {
    loadingSignal.set(false);
    facade.loadOrders.mockReset();
    facade.resetListFilters.mockReset();
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        supplierName: 'Acme Traders',
        supplierReference: 'SUP-REF-001',
        lineCount: 2,
        expectedQuantity: 5,
        receivedQuantity: 3,
        expectedTotal: 1500,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);
    filtersSignal.set(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
    paginationSignal.set({ totalCount: 1, pageNumber: 1, pageSize: 20 });

    TestBed.configureTestingModule({
      imports: [PurchaseOrdersListPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        provideRouter([]),
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: PurchaseOrderService, useValue: {} },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads purchase orders on initialization', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    expect(facade.loadOrders).toHaveBeenCalledWith(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
  });

  it('renders list contents when not loading', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('PO-2026-000001');
    expect(host.textContent).toContain('3 / 5');
    expect(host.textContent).toContain('2');
    expect(host.textContent).toContain('purchaseOrders.title');
  });

  it('shows empty-state text when no purchase orders exist', () => {
    purchaseOrderSignal.set([]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.noResults');
  });

  it('reloads filters when search changes', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    fixture.componentInstance['onSearchChange']('rice');

    expect(facade.loadOrders).toHaveBeenLastCalledWith({ search: 'rice', page: 1 });
  });
});
