import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { PurchaseOrderDetail, PurchaseOrderLine } from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail-page.component';

const purchaseOrderSignal = signal<PurchaseOrderDetail | null>(null);
const loadingDetailSignal = signal(false);
const errorMessageSignal = signal('');
const canManagePurchaseOrdersSignal = signal(true);

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
    placeOrder: vi.fn(),
    deleteDraft: vi.fn(),
    cancelOrder: vi.fn(),
  };

  const permissions = {
    canManagePurchaseOrders: canManagePurchaseOrdersSignal,
  };

  beforeEach(() => {
    facade.loadDetail.mockReset();
    facade.clearDetail.mockReset();
    facade.placeOrder.mockReset();
    facade.deleteDraft.mockReset();
    facade.cancelOrder.mockReset();
    loadingDetailSignal.set(false);
    purchaseOrderSignal.set(null);
    errorMessageSignal.set('');
    canManagePurchaseOrdersSignal.set(true);

    TestBed.configureTestingModule({
      imports: [PurchaseOrderDetailPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: ActivatedRoute, useValue: route },
        { provide: ShopPermissionsService, useValue: permissions },
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

  it('shows edit/place/delete actions for Draft order when Owner/Manager', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.editPo');
    expect(host.textContent).toContain('purchaseOrders.actions.place');
    expect(host.textContent).toContain('purchaseOrders.actions.delete');
  });

  it('hides edit/place/delete actions for Draft when Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.editPo');
    expect(host.textContent).not.toContain('purchaseOrders.actions.place');
    expect(host.textContent).not.toContain('purchaseOrders.actions.delete');
  });

  it('shows cancel form for Placed order when Owner/Manager', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('purchaseOrders.actions.cancel');
    expect(host.textContent).not.toContain('purchaseOrders.editPo');
  });

  it('hides cancel form for Placed order when Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.actions.cancel');
  });

  it('shows no lifecycle actions for Cancelled order', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({
      ...selectedOrder,
      status: 'Cancelled',
      cancellationReason: 'Supplier unavailable',
    });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('purchaseOrders.editPo');
    expect(host.textContent).not.toContain('purchaseOrders.actions.place');
    expect(host.textContent).not.toContain('purchaseOrders.actions.delete');
    expect(host.textContent).not.toContain('purchaseOrders.actions.cancel');
    expect(host.textContent).toContain('Supplier unavailable');
  });

  it('dispatches placeOrder when place button clicked', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const placeButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('purchaseOrders.actions.place'));
    placeButton?.click();

    expect(facade.placeOrder).toHaveBeenCalledWith('po-1');
  });

  it('dispatches deleteDraft when delete button clicked', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const deleteButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('purchaseOrders.actions.delete'));
    deleteButton?.click();

    expect(facade.deleteDraft).toHaveBeenCalledWith('po-1');
  });
});
