import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryBatchDefaultsService } from '../../inventory/services/inventory-batch-defaults.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { PurchaseOrderDetail } from '../services/purchase-order.service';
import { ReceivePurchaseOrderDialogComponent } from './receive-purchase-order-dialog.component';

const order: PurchaseOrderDetail = {
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
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
      description: 'Widget',
      expectedQuantity: 5,
      receivedQuantity: 2,
      remainingQuantity: 3,
      unitCost: 10,
      lineTotal: 50,
    },
    {
      lineId: 'line-2',
      itemId: 'item-2',
      description: 'Complete line',
      expectedQuantity: 2,
      receivedQuantity: 2,
      remainingQuantity: 0,
      unitCost: 4,
      lineTotal: 8,
    },
  ],
  expectedTotal: 58,
  createdAt: '2026-06-01T00:00:00Z',
  cancellationReason: null,
};

describe('ReceivePurchaseOrderDialogComponent', () => {
  let fixture: ComponentFixture<ReceivePurchaseOrderDialogComponent>;
  let batchSequence: number;
  const batchDefaults = {
    generateBatchNumber: vi.fn(() => {
      batchSequence += 1;
      return `BN-${batchSequence}`;
    }),
    lookupHsn: vi.fn(() =>
      Promise.resolve({
        hsnCodes: ['0401'],
        taxScenarios: [{ condition: 'default', taxPercentage: '18%' }],
      }),
    ),
    getAutoTaxRatePercent: vi.fn(() => 18),
  };
  const inventoryService = {
    generateItemBarcode: vi.fn(() => of({ barcode: 'GEN-PO-001' })),
  };
  const catalogSync = {
    catalogEntries: signal([
      { itemId: 'item-1', name: 'Widget', barcode: 'IT-PO-001' },
      { itemId: 'item-2', name: 'Complete line', barcode: 'IT-PO-002' },
    ]),
    filterByBarcode: vi.fn((query: string) =>
      catalogSync
        .catalogEntries()
        .filter((entry) => entry.barcode.toLowerCase().startsWith(query.toLowerCase())),
    ),
  };

  beforeEach(() => {
    batchSequence = 0;
    batchDefaults.generateBatchNumber.mockClear();
    batchDefaults.lookupHsn.mockClear();
    batchDefaults.getAutoTaxRatePercent.mockClear();
    inventoryService.generateItemBarcode.mockReset();
    inventoryService.generateItemBarcode.mockReturnValue(of({ barcode: 'GEN-PO-001' }));
    catalogSync.catalogEntries.set([
      { itemId: 'item-1', name: 'Widget', barcode: 'IT-PO-001' },
      { itemId: 'item-2', name: 'Complete line', barcode: 'IT-PO-002' },
    ]);
    catalogSync.filterByBarcode.mockClear();

    TestBed.configureTestingModule({
      imports: [
        ReceivePurchaseOrderDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              inventory: {
                barcode: 'Barcode',
                openScanner: 'Open scanner',
                generateBarcode: 'Generate',
                generateBarcodeReplaceConfirm: 'Replace current barcode?',
                confirmReplace: 'Replace',
                keepCurrentBarcode: 'Keep current',
                generateBarcodeError: 'Generate barcode failed',
              },
              purchaseOrders: {
                actions: { cancel: 'Cancel', receive: 'Receive' },
                receiveDialog: {
                  title: 'Receive purchase order',
                  line: 'Line',
                  selectLine: 'Select line',
                  barcode: 'Barcode',
                  batchNumber: 'Batch number',
                  generateBatchNumber: 'Generate batch number',
                  quantity: 'Quantity',
                  totalPurchaseCost: 'Total purchase cost',
                  mrp: 'MRP',
                  salesPrice: 'Sales price',
                  taxRate: 'Tax rate',
                  lookupTaxRate: 'Lookup tax rate',
                  expiryDate: 'Expiry date',
                  manufacturingDate: 'Manufacturing date',
                  reference: 'Reference',
                  notes: 'Notes',
                  searchLines: 'Search by product name',
                  taxIncluded: 'Tax included',
                  purchaseTaxIncluded: 'Purchase tax included',
                  quantityOverRemaining: 'Quantity cannot exceed remaining quantity.',
                  addLine: 'Add line',
                  removeLine: 'Remove',
                  duplicateLine: 'Duplicate line',
                },
              },
            },
          },
          translocoConfig: {
            defaultLang: 'en',
            availableLangs: ['en'],
          },
          preloadLangs: true,
        }),
      ],
      providers: [
        { provide: InventoryBatchDefaultsService, useValue: batchDefaults },
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
      ],
    });

    fixture = TestBed.createComponent(ReceivePurchaseOrderDialogComponent);
    fixture.componentRef.setInput('order', order);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('initializes one receipt row for each receivable PO line', () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [order.lines[0], { ...order.lines[1], receivedQuantity: 0, remainingQuantity: 2 }],
    };
    fixture.componentRef.setInput('order', twoLineOrder);
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      receivableLines: readonly { lineId: string }[];
      lines: {
        controls: Array<{
          controls: {
            purchaseOrderLineId: { value: string };
            batchNumber: { value: string };
            quantity: { value: number };
            totalPurchaseCost: { value: number };
          };
        }>;
      };
      remainingFor: (lineId: string) => number;
    };

    expect(component.receivableLines.map((line) => line.lineId)).toEqual(['line-1', 'line-2']);
    expect(component.remainingFor('line-1')).toBe(3);
    expect(component.remainingFor('line-2')).toBe(2);
    expect(component.lines.controls[0].controls.purchaseOrderLineId.value).toBe('line-1');
    expect(component.lines.controls[0].controls.quantity.value).toBe(3);
    expect(component.lines.controls[0].controls.totalPurchaseCost.value).toBe(30);
    expect(component.lines.controls[1].controls.purchaseOrderLineId.value).toBe('line-2');
    expect(component.lines.controls[1].controls.quantity.value).toBe(2);
    expect(component.lines.controls[1].controls.totalPurchaseCost.value).toBe(8);
  });

  it('renders the shared inventory barcode field for each receipt row', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('app-inventory-barcode-field').length).toBe(1);
  });

  it('filters visible receipt rows by product name search without removing form rows', () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [
        order.lines[0],
        { ...order.lines[1], description: 'Rapha 11', receivedQuantity: 0, remainingQuantity: 2 },
      ],
    };
    fixture.componentRef.setInput('order', twoLineOrder);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance as unknown as {
      lines: { length: number };
    };

    const searchInput = host.querySelector<HTMLInputElement>('[data-testid="receive-line-search"]');
    expect(searchInput).toBeTruthy();
    expect(host.querySelectorAll('[data-testid="receipt-row"]').length).toBe(2);

    searchInput!.value = 'rapha';
    searchInput!.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(component.lines.length).toBe(2);
    expect(host.querySelectorAll('[data-testid="receipt-row"]').length).toBe(1);
    expect(host.textContent).toContain('Rapha 11');
    expect(host.textContent).not.toContain('Widget');
  });

  it('looks up the PO line tax using inventory batch defaults', async () => {
    const component = fixture.componentInstance as unknown as {
      lines: { controls: Array<{ controls: { taxRatePercent: { value: number } } }> };
    };

    await fixture.whenStable();

    expect(batchDefaults.lookupHsn).toHaveBeenCalledWith('Widget');
    expect(batchDefaults.getAutoTaxRatePercent).toHaveBeenCalled();
    expect(component.lines.controls[0].controls.taxRatePercent.value).toBe(18);
  });

  it('can regenerate the batch number for a receipt row', () => {
    const component = fixture.componentInstance as unknown as {
      generateBatchNumber: (index: number) => void;
      lines: { controls: Array<{ controls: { batchNumber: { value: string } } }> };
    };

    component.generateBatchNumber(0);

    expect(component.lines.controls[0].controls.batchNumber.value).toBe('BN-3');
  });

  it('prevents submission when quantity exceeds remaining quantity', () => {
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      remainingError: () => boolean;
    };
    const emitSpy = vi.spyOn(component.receive, 'emit');

    component.lines.controls[0].patchValue({
      quantity: 4,
      batchNumber: 'BATCH-1',
      totalPurchaseCost: 40,
      mrp: 12,
      salesPrice: 11,
    });
    fixture.detectChanges();

    expect(component.remainingError()).toBe(true);
    (fixture.componentInstance as unknown as { submit: () => void }).submit();
    expect(emitSpy).not.toHaveBeenCalled();
  });

  it('enforces inbound inventory constraints on receipt rows', () => {
    const component = fixture.componentInstance as unknown as {
      lines: {
        controls: Array<{
          patchValue: (value: unknown) => void;
          valid: boolean;
          hasError: (error: string) => boolean;
          controls: {
            barcode: { valid: boolean };
            taxRatePercent: { hasError: (error: string) => boolean };
          };
        }>;
      };
    };
    const line = component.lines.controls[0];

    line.patchValue({
      barcode: 'B'.repeat(120),
      batchNumber: 'B'.repeat(80),
      quantity: 1,
      totalPurchaseCost: 10,
      mrp: 100,
      salesPrice: 101,
      taxRatePercent: 101,
    });

    expect(line.controls.barcode.valid).toBe(true);
    expect(line.hasError('salesPriceExceedsMrp')).toBe(true);
    expect(line.controls.taxRatePercent.hasError('max')).toBe(true);

    line.patchValue({ barcode: 'B'.repeat(121) });
    expect(line.controls.barcode.valid).toBe(false);
  });

  it('emits one-line receive payload with trimmed optional fields', () => {
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      form: { patchValue: (value: unknown) => void };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      submit: () => void;
    };
    const emitSpy = vi.spyOn(component.receive, 'emit');

    component.form.patchValue({
      referenceNumber: ' REF-1 ',
      notes: ' Dock 2 ',
    });
    component.lines.controls[0].patchValue({
      barcode: ' IT-PO-001 ',
      batchNumber: ' BATCH-1 ',
      quantity: 2,
      totalPurchaseCost: 20,
      mrp: 14,
      salesPrice: 12,
      taxRatePercent: 5,
      taxIncluded: true,
      purchaseTaxIncluded: false,
      expiryDate: new Date(2026, 11, 31),
      manufacturingDate: null,
    });
    component.submit();

    expect(emitSpy).toHaveBeenCalledWith({
      referenceNumber: 'REF-1',
      notes: 'Dock 2',
      receivedAt: null,
      lines: [
        {
          purchaseOrderLineId: 'line-1',
          barcode: 'IT-PO-001',
          batchNumber: 'BATCH-1',
          quantity: 2,
          totalPurchaseCost: 20,
          mrp: 14,
          salesPrice: 12,
          taxRatePercent: 5,
          taxIncluded: true,
          purchaseTaxIncluded: false,
          expiryDate: '2026-12-31',
          manufacturingDate: null,
        },
      ],
    });
  });

  it('emits a multi-line payload from prepopulated receipt rows', () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [order.lines[0], { ...order.lines[1], receivedQuantity: 0, remainingQuantity: 2 }],
    };
    fixture.componentRef.setInput('order', twoLineOrder);
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      submit: () => void;
    };
    const emitSpy = vi.spyOn(component.receive, 'emit');

    component.lines.controls[0].patchValue({
      batchNumber: 'BATCH-1',
      quantity: 1,
      totalPurchaseCost: 10,
      mrp: 12,
      salesPrice: 11,
    });
    component.lines.controls[0].patchValue({ barcode: 'IT-PO-001' });
    component.lines.controls[1].patchValue({
      barcode: 'IT-PO-002',
      batchNumber: 'BATCH-2',
      quantity: 2,
      totalPurchaseCost: 8,
      mrp: 6,
      salesPrice: 5,
    });
    component.submit();

    expect(emitSpy.mock.calls[0][0].lines).toEqual([
      expect.objectContaining({
        purchaseOrderLineId: 'line-1',
        barcode: 'IT-PO-001',
        batchNumber: 'BATCH-1',
      }),
      expect.objectContaining({
        purchaseOrderLineId: 'line-2',
        barcode: 'IT-PO-002',
        batchNumber: 'BATCH-2',
      }),
    ]);
  });

  it('uses PrimeNG date pickers for receipt dates', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('p-datepicker').length).toBe(2);
    expect(host.querySelector('input[type="date"]')).toBeNull();
  });
});
