import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { PurchaseOrderDetail, PurchaseOrderLine } from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail-page.component';

const purchaseOrderSignal = signal<PurchaseOrderDetail | null>(null);
const loadingDetailSignal = signal(false);
const errorMessageSignal = signal('');

const orderLines: readonly PurchaseOrderLine[] = [
  {
    lineId: 'line-1',
    itemId: 'item-1',
    description: 'Widget',
    expectedQuantity: 3,
    unitCost: 50,
    lineTotal: 150,
  },
  {
    lineId: 'line-2',
    itemId: 'item-2',
    description: 'Service Fee',
    expectedQuantity: 1,
    unitCost: 25,
    lineTotal: 25,
  },
];

const selectedOrder: PurchaseOrderDetail = {
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Draft',
  supplierId: null,
  orderDate: null,
  expectedDeliveryDate: null,
  supplierReferenceNumber: null,
  notes: 'First draft',
  lines: orderLines,
  expectedTotal: 175,
  createdAt: '2026-06-01T00:00:00Z',
  cancellationReason: null,
};

const route = {
  snapshot: {
    paramMap: {
      get: (key: string) => (key === 'purchaseOrderId' ? 'po-1' : null),
    },
  },
};

describe('PurchaseOrderDetailPageComponent', () => {
  const facade = {
    selectedOrder: purchaseOrderSignal,
    isLoadingDetail: loadingDetailSignal,
    errorMessage: errorMessageSignal,
    loadDetail: vi.fn(),
    clearDetail: vi.fn(),
  };

  beforeEach(() => {
    facade.loadDetail.mockReset();
    facade.clearDetail.mockReset();
    loadingDetailSignal.set(false);
    purchaseOrderSignal.set(null);
    errorMessageSignal.set('');

    TestBed.configureTestingModule({
      imports: [PurchaseOrderDetailPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: ActivatedRoute, useValue: route },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads detail when route has purchaseOrderId', () => {
    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    expect(facade.loadDetail).toHaveBeenCalledWith('po-1');
  });

  it('renders purchase order detail when loaded', () => {
    purchaseOrderSignal.set(selectedOrder);
    loadingDetailSignal.set(false);

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('PO-2026-000001');
    expect(host.textContent).toContain('Service Fee');
    expect(host.textContent).toContain('175');
  });

  it('renders error message when loading fails', () => {
    loadingDetailSignal.set(false);
    purchaseOrderSignal.set(null);
    errorMessageSignal.set('purchaseOrders.errors.notFound');

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.errors.notFound');
  });

  it('clears detail on destroy', () => {
    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    const component = fixture.componentInstance;

    fixture.destroy();

    expect(facade.clearDetail).toHaveBeenCalled();
    expect(component).toBeDefined();
  });
});
