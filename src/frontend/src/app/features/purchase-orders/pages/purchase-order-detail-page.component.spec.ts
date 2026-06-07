import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { ConfirmationService } from 'primeng/api';
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
  supplierName: null,
  supplierReference: null,
  receivedQuantity: 0,
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
      imports: [
        PurchaseOrderDetailPageComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              purchaseOrders: {
                editPo: 'Edit Purchase Order Draft',
                status: 'Status',
                expectedTotal: 'Expected Total',
                actions: {
                  placeOrder: 'Place Order',
                  deleteDraft: 'Delete Draft',
                  cancelOrder: 'Cancel Order',
                },
                dialog: {
                  deleteDraftTitle: 'Delete Draft',
                  deleteDraftBody: 'Are you sure you want to delete this draft purchase order?',
                  cancelReasonLabel: 'Reason (required)'
                }
              }
            }
          },
          translocoConfig: {
            defaultLang: 'en',
            availableLangs: ['en'],
          },
          preloadLangs: true
        })
      ],
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
    expect(host.textContent).toContain('Edit Purchase Order Draft');
    expect(host.textContent).toContain('Place Order');
    expect(host.textContent).toContain('Delete Draft');
  });

  it('hides edit/place/delete actions for Draft when Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('Edit Purchase Order Draft');
    expect(host.textContent).not.toContain('Place Order');
    expect(host.textContent).not.toContain('Delete Draft');
  });

  it('shows cancel form for Placed order when Owner/Manager', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Cancel Order');
    expect(host.textContent).not.toContain('Edit Purchase Order Draft');

    const input = host.querySelector('input') as HTMLInputElement;
    expect(input).toBeTruthy();
    expect(input.placeholder).toBe('Reason (required)');
  });

  it('hides cancel form for Placed order when Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('Cancel Order');
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
    expect(host.textContent).not.toContain('Edit Purchase Order Draft');
    expect(host.textContent).not.toContain('Place Order');
    expect(host.textContent).not.toContain('Delete Draft');
    expect(host.textContent).not.toContain('Cancel Order');
    expect(host.textContent).toContain('Supplier unavailable');
  });

  it('dispatches placeOrder when place button clicked', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const placeButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('Place Order'));
    placeButton?.click();

    expect(facade.placeOrder).toHaveBeenCalledWith('po-1');
  });

  it('confirms before dispatching deleteDraft when delete button clicked', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    const confirmationService = fixture.debugElement.injector.get(ConfirmationService);
    const confirmSpy = vi.spyOn(confirmationService, 'confirm');
    fixture.detectChanges();

    const deleteButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('Delete Draft'));
    deleteButton?.click();

    expect(confirmSpy).toHaveBeenCalled();
    const call = confirmSpy.mock.calls[0][0];
    expect(call.header).toBe('Delete Draft');
    expect(call.message).toBe('Are you sure you want to delete this draft purchase order?');
    expect(typeof call.accept).toBe('function');
    expect(facade.deleteDraft).not.toHaveBeenCalled();

    call.accept?.();
    expect(facade.deleteDraft).toHaveBeenCalledWith('po-1');
  });

  it('enables/disables cancel button based on reason and dispatches cancelOrder on click', async () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('input') as HTMLInputElement;
    const cancelBtn = Array.from(host.querySelectorAll('button'))
      .find((btn) => btn.textContent?.includes('Cancel Order')) as HTMLButtonElement;

    expect(cancelBtn).toBeTruthy();
    expect(cancelBtn.disabled).toBe(true); // initially disabled since reason is empty

    // Input some whitespace
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(cancelBtn.disabled).toBe(true); // still disabled for whitespace

    // Input a valid reason
    input.value = 'Ordered wrong items';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(cancelBtn.disabled).toBe(false); // enabled now

    // Click it
    cancelBtn.click();
    expect(facade.cancelOrder).toHaveBeenCalledWith('po-1', { reason: 'Ordered wrong items' });
  });
});
