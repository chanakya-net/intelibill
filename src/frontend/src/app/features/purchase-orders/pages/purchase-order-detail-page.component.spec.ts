import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { ConfirmationService } from 'primeng/api';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { PurchaseOrderDraftIndexedDbService } from '../../../core/storage/purchase-order-draft-indexeddb.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { PurchaseOrderDetail, PurchaseOrderLine } from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail-page.component';

const purchaseOrderSignal = signal<PurchaseOrderDetail | null>(null);
const loadingDetailSignal = signal(false);
const errorMessageSignal = signal('');
const canManagePurchaseOrdersSignal = signal(true);
const sessionSignal = signal({ activeShopId: 'shop-1' });
const suppliersSignal = signal([]);

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
    updateDraft: vi.fn(),
    createDraft: vi.fn(),
    deleteDraft: vi.fn(),
    cancelOrder: vi.fn(),
    closeOrder: vi.fn(),
    receiveOrder: vi.fn(),
    isSubmitting: signal(false),
  };

  const permissions = {
    canManagePurchaseOrders: canManagePurchaseOrdersSignal,
  };
  const router = {
    navigate: vi.fn(),
  };
  const originalOpen = window.open;

  beforeEach(() => {
    facade.loadDetail.mockReset();
    facade.clearDetail.mockReset();
    facade.placeOrder.mockReset();
    facade.deleteDraft.mockReset();
    facade.cancelOrder.mockReset();
    facade.closeOrder.mockReset();
    router.navigate.mockReset();
    facade.receiveOrder.mockReset();
    window.open = vi.fn();
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
                statusLabel: 'Status',
                expectedTotal: 'Expected Total',
                status: {
                  Draft: 'Draft',
                  Placed: 'Placed',
                  Cancelled: 'Cancelled',
                  PartiallyReceived: 'Partially received',
                  Received: 'Received',
                  Closed: 'Closed',
                },
                actions: {
                  print: 'Print',
                  placeOrder: 'Place Order',
                  deleteDraft: 'Delete Draft',
                  cancelOrder: 'Cancel Order',
                  receive: 'Receive',
                  close: 'Close Order',
                },
                closeReason: 'Close reason',
                receivedQuantity: 'Received',
                remainingQuantity: 'Remaining',
                receipts: {
                  title: 'Receipts',
                  receipt: 'Receipt',
                  receivedAt: 'Received at',
                  quantity: 'Quantity',
                  totalCost: 'Total cost',
                  batch: 'Batch',
                  unitCost: 'Unit cost',
                  pricing: 'Sales / MRP',
                  tax: 'Tax',
                  stockTransaction: 'Stock transaction',
                  voided: 'Voided',
                },
                receiveDialog: {
                  title: 'Receive purchase order',
                  line: 'Line',
                  selectLine: 'Select line',
                  batchNumber: 'Batch number',
                  quantity: 'Quantity',
                  totalPurchaseCost: 'Total purchase cost',
                  mrp: 'MRP',
                  salesPrice: 'Sales price',
                  taxRate: 'Tax rate',
                  expiryDate: 'Expiry date',
                  manufacturingDate: 'Manufacturing date',
                  reference: 'Reference',
                  notes: 'Notes',
                  taxIncluded: 'Tax included',
                  purchaseTaxIncluded: 'Purchase tax included',
                  quantityOverRemaining: 'Quantity cannot exceed remaining quantity.',
                  addLine: 'Add line',
                  removeLine: 'Remove',
                  duplicateLine: 'Duplicate line',
                },
                dialog: {
                  deleteDraftTitle: 'Delete Draft',
                  deleteDraftBody: 'Are you sure you want to delete this draft purchase order?',
                  cancelReasonLabel: 'Reason (required)',
                  closeReasonLabel: 'Close reason (required)'
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
        { provide: AuthService, useValue: { session: sessionSignal } },
        { provide: ActivatedRoute, useValue: route },
        { provide: ShopPermissionsService, useValue: permissions },
        { provide: Router, useValue: router },
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
        { provide: InventoryService, useValue: { generateItemBarcode: () => of({ barcode: 'BAR-1' }), addItem: vi.fn() } },
      ],
    });
  });

  afterEach(() => {
    window.open = originalOpen;
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

  it('opens the edit overlay from the detail page', async () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Draft' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const editButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('Edit Purchase Order Draft'));
    expect(editButton).toBeTruthy();

    editButton?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));

    expect(facade.loadDetail).toHaveBeenLastCalledWith('po-1');
    expect(fixture.nativeElement.querySelector('app-purchase-order-builder-page')).toBeTruthy();
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

  it('shows receive action for placed order and dispatches receiveOrder from dialog submit', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({
      ...selectedOrder,
      status: 'Placed',
      lines: [{ ...orderLines[0], receivedQuantity: 0, remainingQuantity: 3 }],
    });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const receiveButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('Receive')) as HTMLButtonElement;
    expect(receiveButton).toBeTruthy();

    receiveButton.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('app-receive-purchase-order-dialog');
    expect(dialog).toBeTruthy();
    const component = fixture.componentInstance as unknown as {
      receiveOrder: (purchaseOrderId: string, payload: unknown) => void;
    };
    const payload = {
      referenceNumber: null,
      notes: null,
      receivedAt: null,
      lines: [{
        purchaseOrderLineId: 'line-1',
        barcode: 'IT-PO-001',
        batchNumber: 'BATCH-1',
        quantity: 1,
        totalPurchaseCost: 50,
        mrp: 60,
        salesPrice: 55,
        taxRatePercent: 0,
        taxIncluded: false,
        purchaseTaxIncluded: false,
        expiryDate: null,
        manufacturingDate: null,
      }],
    };
    component.receiveOrder('po-1', payload);

    expect(facade.receiveOrder).toHaveBeenCalledWith('po-1', payload);
  });

  it('hides cancel form for Placed order when Staff', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).not.toContain('Cancel Order');
  });

  it('shows close action only for partially received order and dispatches closeOrder', () => {
    canManagePurchaseOrdersSignal.set(true);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'PartiallyReceived' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Close Order');
    const input = host.querySelector('input[placeholder="Close reason (required)"]') as HTMLInputElement;
    const closeBtn = Array.from(host.querySelectorAll('button'))
      .find((btn) => btn.textContent?.includes('Close Order')) as HTMLButtonElement;

    expect(closeBtn.disabled).toBe(true);
    input.value = 'Supplier short shipped';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    closeBtn.click();

    expect(facade.closeOrder).toHaveBeenCalledWith('po-1', { reason: 'Supplier short shipped' });
  });

  it('renders receipt history with batch metadata', () => {
    purchaseOrderSignal.set({
      ...selectedOrder,
      status: 'PartiallyReceived',
      receipts: [{
        receiptId: 'receipt-1',
        receiptNumber: 'POR-2026-000001',
        receivedAt: '2026-06-07T10:00:00Z',
        referenceNumber: 'REF-1',
        notes: null,
        receivedByUserId: 'user-1',
        receivedByDisplayName: null,
        lines: [{
          receiptLineId: 'receipt-line-1',
          purchaseOrderLineId: 'line-1',
          itemId: 'item-1',
          inventoryBatchId: 'batch-1',
          batchNumber: 'BATCH-1',
          batchVoided: true,
          stockTransactionId: 'stock-1',
          quantity: 1,
          totalPurchaseCost: 10,
          unitCost: 10,
          mrp: 12,
          salesPrice: 11,
          taxRatePercent: 5,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        }],
      }],
    });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('POR-2026-000001');
    expect(host.textContent).toContain('BATCH-1');
    expect(host.textContent).toContain('Voided');
    expect(host.textContent).toContain('stock-1');
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

  it.each(['Draft', 'Placed', 'Cancelled'] as const)('shows print action for %s order without mutation permission', (status) => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Print');
  });

  it('opens print route in a new browser tab when print action is clicked', () => {
    canManagePurchaseOrdersSignal.set(false);
    purchaseOrderSignal.set({ ...selectedOrder, status: 'Placed' });

    const fixture = TestBed.createComponent(PurchaseOrderDetailPageComponent);
    fixture.detectChanges();

    const printButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((btn) => btn.textContent?.includes('Print'));
    printButton?.click();

    expect(window.open).toHaveBeenCalledWith('/inventory/purchase-orders/po-1/print', '_blank');
    expect(router.navigate).not.toHaveBeenCalled();
    expect(facade.placeOrder).not.toHaveBeenCalled();
    expect(facade.deleteDraft).not.toHaveBeenCalled();
    expect(facade.cancelOrder).not.toHaveBeenCalled();
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
