import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';

import { InventoryBatchDefaultsService } from '../../inventory/services/inventory-batch-defaults.service';
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

  beforeEach(() => {
    batchSequence = 0;
    batchDefaults.generateBatchNumber.mockClear();
    batchDefaults.lookupHsn.mockClear();
    batchDefaults.getAutoTaxRatePercent.mockClear();

    TestBed.configureTestingModule({
      imports: [
        ReceivePurchaseOrderDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              purchaseOrders: {
                actions: { cancel: 'Cancel', receive: 'Receive' },
                receiveDialog: {
                  title: 'Receive purchase order',
                  line: 'Line',
                  selectLine: 'Select line',
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
      ],
    });

    fixture = TestBed.createComponent(ReceivePurchaseOrderDialogComponent);
    fixture.componentRef.setInput('order', order);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('initializes with only receivable lines and remaining quantity', () => {
    const component = fixture.componentInstance as unknown as {
      receivableLines: readonly { lineId: string }[];
      lines: { controls: Array<{ controls: { purchaseOrderLineId: { value: string }, batchNumber: { value: string }, quantity: { value: number }, totalPurchaseCost: { value: number } } }> };
      remainingFor: (lineId: string) => number;
    };

    expect(component.receivableLines.map((line) => line.lineId)).toEqual(['line-1']);
    expect(component.remainingFor('line-1')).toBe(3);
    expect(component.lines.controls[0].controls.purchaseOrderLineId.value).toBe('line-1');
    expect(component.lines.controls[0].controls.batchNumber.value).toBe('BN-2');
    expect(component.lines.controls[0].controls.quantity.value).toBe(3);
    expect(component.lines.controls[0].controls.totalPurchaseCost.value).toBe(30);
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

    component.lines.controls[0].patchValue({ quantity: 4, batchNumber: 'BATCH-1', totalPurchaseCost: 40, mrp: 12, salesPrice: 11 });
    fixture.detectChanges();

    expect(component.remainingError()).toBe(true);
    (fixture.componentInstance as unknown as { submit: () => void }).submit();
    expect(emitSpy).not.toHaveBeenCalled();
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
      lines: [{
        purchaseOrderLineId: 'line-1',
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
      }],
    });
  });

  it('adds a second receipt row and emits a multi-line payload', () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [
        order.lines[0],
        { ...order.lines[1], receivedQuantity: 0, remainingQuantity: 2 },
      ],
    };
    fixture.componentRef.setInput('order', twoLineOrder);
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      addLine: () => void;
      submit: () => void;
    };
    const emitSpy = vi.spyOn(component.receive, 'emit');

    component.lines.controls[0].patchValue({ batchNumber: 'BATCH-1', quantity: 1, totalPurchaseCost: 10, mrp: 12, salesPrice: 11 });
    component.addLine();
    component.lines.controls[1].patchValue({ batchNumber: 'BATCH-2', quantity: 2, totalPurchaseCost: 8, mrp: 6, salesPrice: 5 });
    component.submit();

    expect(emitSpy.mock.calls[0][0].lines).toEqual([
      expect.objectContaining({ purchaseOrderLineId: 'line-1', batchNumber: 'BATCH-1' }),
      expect.objectContaining({ purchaseOrderLineId: 'line-2', batchNumber: 'BATCH-2' }),
    ]);
  });

  it('uses PrimeNG date pickers for receipt dates', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('p-datepicker').length).toBe(2);
    expect(host.querySelector('input[type="date"]')).toBeNull();
  });
});
