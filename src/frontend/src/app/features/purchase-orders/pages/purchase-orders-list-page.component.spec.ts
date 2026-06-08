import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { PurchaseOrderDraftIndexedDbService } from '../../../core/storage/purchase-order-draft-indexeddb.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderDetail,
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
const selectedOrderSignal = signal<PurchaseOrderDetail | null>(null);
const createSucceededSignal = signal(false);
const errorMessageSignal = signal('');
const filtersSignal = signal(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
const paginationSignal = signal({ totalCount: 1, pageNumber: 1, pageSize: 20 });
const canManagePurchaseOrdersSignal = signal(true);
const sessionSignal = signal({ activeShopId: 'shop-1' });
const suppliersSignal = signal([]);

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
  updateDraft: vi.fn(),
  placeOrder: vi.fn(),
  receiveOrder: vi.fn(),
  deleteDraft: vi.fn(),
  clearDetail: vi.fn(),
  clearError: vi.fn(),
  resetListFilters: vi.fn(),
};

const permissions = {
  canManagePurchaseOrders: canManagePurchaseOrdersSignal,
};

const router = {
  navigate: vi.fn(),
};

describe('PurchaseOrdersListPageComponent', () => {
  beforeEach(() => {
    loadingSignal.set(false);
    selectedOrderSignal.set(null);
    canManagePurchaseOrdersSignal.set(true);
    facade.loadOrders.mockReset();
    facade.loadDetail.mockReset();
    facade.placeOrder.mockReset();
    facade.receiveOrder.mockReset();
    facade.deleteDraft.mockReset();
    facade.resetListFilters.mockReset();
    router.navigate.mockReset();
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
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: { session: sessionSignal } },
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: PurchaseOrderService, useValue: {} },
        {
          provide: PurchaseOrderDraftIndexedDbService,
          useValue: {
            loadDraft: vi.fn(async () => null),
            saveDraft: vi.fn(async () => undefined),
            clearDraft: vi.fn(async () => undefined),
          },
        },
        { provide: SuppliersFacade, useValue: { suppliers: suppliersSignal, load: vi.fn() } },
        { provide: ProductCatalogSyncService, useValue: { filterByName: () => [], findByName: () => null, upsertEntry: vi.fn() } },
        { provide: InventoryService, useValue: { generateItemBarcode: vi.fn(), addItem: vi.fn() } },
        { provide: ShopPermissionsService, useValue: permissions },
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

  it('reloads filters when status changes', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    fixture.componentInstance['onStatusChange']('Draft');

    expect(facade.loadOrders).toHaveBeenLastCalledWith({ status: 'Draft', page: 1 });
  });

  it('reloads filters when order date range changes', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();
    const fromDate = new Date(2026, 5, 1);
    const toDate = new Date(2026, 5, 30);

    fixture.componentInstance['onOrderDateFromChange'](fromDate);
    fixture.componentInstance['onOrderDateToChange'](toDate);

    expect(facade.loadOrders).toHaveBeenNthCalledWith(2, { orderDateFrom: '2026-06-01', page: 1 });
    expect(facade.loadOrders).toHaveBeenNthCalledWith(3, { orderDateTo: '2026-06-30', page: 1 });
    expect(fixture.componentInstance['orderDateFromValue']()).toBe(fromDate);
    expect(fixture.componentInstance['orderDateToValue']()).toBe(toDate);
  });

  it('uses PrimeNG date pickers for date filters', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('p-datepicker').length).toBe(2);
    expect(host.querySelector('input[type="date"]')).toBeNull();
  });

  it('clears filters back to defaults', () => {
    filtersSignal.set({
      search: 'rice',
      status: 'Draft',
      orderDateFrom: '2026-06-01',
      orderDateTo: '2026-06-30',
      page: 2,
      pageSize: 50,
    });
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();
    fixture.componentInstance['onOrderDateFromChange'](new Date(2026, 5, 1));
    fixture.componentInstance['onOrderDateToChange'](new Date(2026, 5, 30));

    fixture.componentInstance['clearFilters']();

    expect(facade.resetListFilters).toHaveBeenCalledTimes(1);
    expect(facade.loadOrders).toHaveBeenLastCalledWith(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
    expect(fixture.componentInstance['orderDateFromValue']()).toBeNull();
    expect(fixture.componentInstance['orderDateToValue']()).toBeNull();
  });

  it('reloads requested page and page size from paginator events', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    fixture.componentInstance['onPageChange']({ page: 2, rows: 20 });
    fixture.componentInstance['onPageChange']({ page: 0, rows: 50 });

    expect(facade.loadOrders).toHaveBeenNthCalledWith(2, { page: 3, pageSize: 20 });
    expect(facade.loadOrders).toHaveBeenNthCalledWith(3, { page: 1, pageSize: 50 });
  });

  it('navigates when a purchase-order row is clicked', async () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('tbody tr') as HTMLTableRowElement;
    row.click();
    await fixture.whenStable();

    expect(router.navigate).toHaveBeenCalledWith(['/inventory/purchase-orders', 'po-1']);
  });

  it('shows new-PO button for Owner/Manager', () => {
    canManagePurchaseOrdersSignal.set(true);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const newPoButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('purchaseOrders.newPo'));

    expect(newPoButton).toBeTruthy();
    expect(newPoButton?.classList.contains('p-button')).toBe(true);
  });

  it('hides new-PO button for Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.newPo');
  });

  it('shows edit link for Draft order when Owner/Manager', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        supplierName: null,
        supplierReference: null,
        lineCount: 1,
        expectedQuantity: 1,
        receivedQuantity: 0,
        expectedTotal: 100,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.editPo');
    expect(host.textContent).toContain('purchaseOrders.actions.placeOrder');
  });

  it('places a Draft order from the list without opening detail', () => {
    canManagePurchaseOrdersSignal.set(true);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const placeButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('purchaseOrders.actions.placeOrder')) as HTMLButtonElement;

    expect(placeButton).toBeTruthy();
    placeButton.click();

    expect(router.navigate).not.toHaveBeenCalled();
    expect(facade.placeOrder).toHaveBeenCalledWith('po-1');
  });

  it('opens the edit overlay without navigating to the detail page', async () => {
    canManagePurchaseOrdersSignal.set(true);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const editButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('purchaseOrders.editPo')) as HTMLButtonElement;

    expect(editButton).toBeTruthy();
    editButton.click();
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));

    expect(router.navigate).not.toHaveBeenCalled();
    expect(facade.loadDetail).toHaveBeenCalledWith('po-1');
    expect(host.querySelector('app-purchase-order-builder-page')).toBeTruthy();
  });

  it('shows delete action for Draft order and confirms before deleting', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        supplierName: null,
        supplierReference: null,
        lineCount: 1,
        expectedQuantity: 1,
        receivedQuantity: 0,
        expectedTotal: 100,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const deleteButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('purchaseOrders.actions.deleteDraft')) as HTMLButtonElement;

    expect(deleteButton).toBeTruthy();
    deleteButton.click();

    expect(facade.deleteDraft).not.toHaveBeenCalled();
    fixture.componentInstance['confirmDeleteDraft']('po-1');
    expect(facade.deleteDraft).toHaveBeenCalledWith('po-1');
  });

  it('hides edit link for non-Draft order', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-2',
        purchaseOrderNumber: 'PO-2026-000002',
        status: 'Placed',
        supplierName: null,
        supplierReference: null,
        lineCount: 1,
        expectedQuantity: 3,
        receivedQuantity: 0,
        expectedTotal: 300,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.editPo');
    expect(host.textContent).not.toContain('purchaseOrders.actions.placeOrder');
    expect(host.textContent).not.toContain('purchaseOrders.actions.deleteDraft');
  });

  it('shows receive action for Placed orders and opens the receipt dialog', async () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-2',
        purchaseOrderNumber: 'PO-2026-000002',
        status: 'Placed',
        supplierName: null,
        supplierReference: null,
        lineCount: 1,
        expectedQuantity: 3,
        receivedQuantity: 0,
        expectedTotal: 300,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const receiveButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('purchaseOrders.actions.receive')) as HTMLButtonElement;

    expect(receiveButton).toBeTruthy();
    receiveButton.click();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(facade.loadDetail).toHaveBeenCalledWith('po-2');

    selectedOrderSignal.set(makeDetail({ purchaseOrderId: 'po-2', status: 'Placed' }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.querySelector('app-receive-purchase-order-dialog')).toBeTruthy();
  });

  it('submits receipt payload from the list receive dialog', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    const payload = {
      referenceNumber: null,
      notes: null,
      receivedAt: null,
      lines: [],
    };

    fixture.componentInstance['receiveOrder']('po-2', payload);

    expect(facade.receiveOrder).toHaveBeenCalledWith('po-2', payload);
    expect(fixture.componentInstance['showReceiveDialog']()).toBe(false);
  });

  it('hides edit link for Staff even on Draft order', () => {
    canManagePurchaseOrdersSignal.set(false);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.editPo');
  });
});

function makeDetail(overrides: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail {
  return {
    purchaseOrderId: 'po-2',
    purchaseOrderNumber: 'PO-2026-000002',
    status: 'Placed',
    supplierId: null,
    supplierName: null,
    supplierReference: null,
    receivedQuantity: 0,
    orderDate: null,
    expectedDeliveryDate: null,
    supplierReferenceNumber: null,
    notes: null,
    lines: [
      {
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Rice',
        expectedQuantity: 3,
        receivedQuantity: 0,
        remainingQuantity: 3,
        unitCost: 100,
        lineTotal: 300,
      },
    ],
    expectedTotal: 300,
    createdAt: '2026-06-01T00:00:00Z',
    cancellationReason: null,
    ...overrides,
  };
}
