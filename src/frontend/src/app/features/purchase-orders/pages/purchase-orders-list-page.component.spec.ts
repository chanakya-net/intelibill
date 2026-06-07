import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { provideRouter } from '@angular/router';

import { PurchaseOrderListItem, PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrdersListPageComponent } from './purchase-orders-list-page.component';

const purchaseOrderSignal = signal<readonly PurchaseOrderListItem[]>([
  {
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-000001',
    status: 'Draft',
    lineCount: 2,
    expectedTotal: 1500,
    createdAt: '2026-06-01T00:00:00Z',
  },
]);

const loadingSignal = signal(false);
const detailLoadingSignal = signal(false);
const selectedOrderSignal = signal(null);
const createSucceededSignal = signal(false);
const errorMessageSignal = signal('');

const facade = {
  orders: purchaseOrderSignal,
  isLoadingList: loadingSignal,
  isLoadingDetail: detailLoadingSignal,
  selectedOrder: selectedOrderSignal,
  createSucceeded: createSucceededSignal,
  isSubmitting: signal(false),
  errorMessage: errorMessageSignal,
  loadOrders: vi.fn(),
  loadDetail: vi.fn(),
  createDraft: vi.fn(),
  clearDetail: vi.fn(),
  clearError: vi.fn(),
};

describe('PurchaseOrdersListPageComponent', () => {
  beforeEach(() => {
    loadingSignal.set(false);
    facade.loadOrders.mockReset();
    purchaseOrderSignal.set([
      {
        purchaseOrderId: 'po-1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        lineCount: 2,
        expectedTotal: 1500,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ]);

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

    expect(facade.loadOrders).toHaveBeenCalledTimes(1);
  });

  it('renders list contents when not loading', () => {
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('PO-2026-000001');
    expect(host.textContent).toContain('2');
    expect(host.textContent).toContain('purchaseOrders.title');
  });

  it('shows empty-state text when no purchase orders exist', () => {
    purchaseOrderSignal.set([]);
    const fixture = TestBed.createComponent(PurchaseOrdersListPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.noDrafts');
  });
});
