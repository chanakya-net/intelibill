import { LocalizationService } from '../../../core/i18n/localization.service';
import { knownExpiryPolicyProviders } from '../../../../tests/expiry/expiry-policy.providers';
import { HttpErrorResponse } from '@angular/common/http';
import { IDBFactory } from 'fake-indexeddb';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { ReceivingDraftService } from '../services/receiving-draft.service';
import { ReceivingOperationState, receivingKey } from '../services/receiving.models';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
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

beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [{ provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } }],
  });
});

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

  const results = signal<Record<string, ReceivingOperationState>>({});
  const facade = {
    receivingState: (c: Parameters<typeof receivingKey>[0]) => results()[receivingKey(c)] ?? null,
    checkReceipt: vi.fn(),
  };

  beforeEach(async () => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    results.set({});
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
        ...knownExpiryPolicyProviders(),
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: InventoryBatchDefaultsService, useValue: batchDefaults },
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
      ],
    });

    TestBed.inject(ShopContextService).initialize({ userId: 'user-1', shopId: 'shop-1' });
    fixture = TestBed.createComponent(ReceivePurchaseOrderDialogComponent);
    fixture.componentRef.setInput('order', order);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await vi.waitFor(() =>
      expect((fixture.componentInstance as unknown as { loading: () => boolean }).loading()).toBe(
        false,
      ),
    );
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('initializes one receipt row for each receivable PO line', async () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [order.lines[0], { ...order.lines[1], receivedQuantity: 0, remainingQuantity: 2 }],
    };
    fixture.componentRef.setInput('order', { ...twoLineOrder, purchaseOrderId: 'po-2' });
    fixture.detectChanges();
    await vi.waitFor(() =>
      expect((fixture.componentInstance as unknown as { loading: () => boolean }).loading()).toBe(
        false,
      ),
    );
    await fixture.whenStable();
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

  it('filters visible receipt rows by product name search without removing form rows', async () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [
        order.lines[0],
        { ...order.lines[1], description: 'Rapha 11', receivedQuantity: 0, remainingQuantity: 2 },
      ],
    };
    fixture.componentRef.setInput('order', { ...twoLineOrder, purchaseOrderId: 'po-2' });
    fixture.detectChanges();
    await vi.waitFor(() =>
      expect((fixture.componentInstance as unknown as { loading: () => boolean }).loading()).toBe(
        false,
      ),
    );
    await fixture.whenStable();
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

  it('emits one-line receive payload with trimmed optional fields', async () => {
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      form: { patchValue: (value: unknown) => void };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      submit: () => Promise<void>;
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
    await component.submit();

    expect(emitSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        payload: expect.objectContaining({
          referenceNumber: 'REF-1',
          notes: 'Dock 2',
          receivedAt: null,
          lines: [
            {
              clientLineId: expect.any(String),
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
        }),
      }),
    );
  });

  it('emits a multi-line payload from prepopulated receipt rows', async () => {
    const twoLineOrder: PurchaseOrderDetail = {
      ...order,
      lines: [order.lines[0], { ...order.lines[1], receivedQuantity: 0, remainingQuantity: 2 }],
    };
    fixture.componentRef.setInput('order', { ...twoLineOrder, purchaseOrderId: 'po-2' });
    fixture.detectChanges();
    await vi.waitFor(() =>
      expect((fixture.componentInstance as unknown as { loading: () => boolean }).loading()).toBe(
        false,
      ),
    );
    await fixture.whenStable();
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      receive: { emit: ReturnType<typeof vi.fn> };
      lines: { controls: Array<{ patchValue: (value: unknown) => void }> };
      submit: () => Promise<void>;
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
    await component.submit();

    expect(emitSpy.mock.calls[0][0].payload.lines).toEqual([
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
  it('accepts grouped lots 4 + 6, rejects 6 + 6, and preserves each row ID on removal', async () => {
    const c = fixture.componentInstance;
    fixture.componentRef.setInput('order', {
      ...order,
      lines: [{ ...order.lines[0], remainingQuantity: 10 }],
    });
    fixture.detectChanges();
    c['lines'].at(0).patchValue({ quantity: 4, batchNumber: 'A' });
    const firstId = c['lines'].at(0).controls.clientLineId.value;
    c['addLine']();
    c['lines'].at(1).patchValue({ quantity: 6, batchNumber: 'B' });
    const secondId = c['lines'].at(1).controls.clientLineId.value;
    expect(c['remainingError']()).toBe(false);
    expect(c['duplicateLotError']()).toBe(false); // Same product barcode on both lots is valid.
    c['lines'].at(0).controls.quantity.setValue(6);
    expect(c['remainingError']()).toBe(true);
    c['removeLine'](0);
    expect(c['lines'].at(0).controls.clientLineId.value).toBe(secondId);
    expect(secondId).not.toBe(firstId);
  });

  it('preserves every edited field and client ID when the same PO refreshes', async () => {
    const c = fixture.componentInstance;
    c['form'].patchValue({ referenceNumber: 'REF', notes: 'notes' });
    c['lines'].at(0).patchValue({
      quantity: 2,
      totalPurchaseCost: 3,
      mrp: 4,
      salesPrice: 2,
      taxRatePercent: 6,
      taxIncluded: true,
      purchaseTaxIncluded: false,
      expiryDate: new Date(2027, 4, 21),
      manufacturingDate: new Date(2026, 4, 20),
    });
    const before = c['form'].getRawValue();
    fixture.componentRef.setInput('order', {
      ...order,
      lines: [{ ...order.lines[0], remainingQuantity: 2, unitCost: 99 }],
    });
    fixture.detectChanges();
    expect(c['form'].getRawValue()).toEqual(before);
    expect(c['remainingFor']('line-1')).toBe(2);
    await c['hide']();
    const record = await TestBed.inject(ReceivingDraftService).load(
      { userId: 'user-1', shopId: 'shop-1' },
      'po-1',
    );
    expect(record!.draft.lines[0].expiryDate).toBe('2027-05-21');
    expect(record!.draft.notes).toBe('notes');
  });

  it('refuses invalid receipt date order before freezing or sending the PO group', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    c['lines']
      .at(0)
      .patchValue({
        quantity: 1,
        expiryDate: new Date(2026, 10, 8),
        manufacturingDate: new Date(2026, 10, 9),
      });
    await c['submit']();
    expect(emitted).not.toHaveBeenCalled();
    expect(c['errorMessage']()).toBe('expiry.dateOrder');
  });

  it('stays open on submission and rejection; only a matching confirmed operation closes it', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    const closed = vi.spyOn(c.closed, 'emit');
    c['lines'].at(0).controls.quantity.setValue(1);
    await c['submit']();
    expect(c.visible).toBe(true);
    expect(closed).not.toHaveBeenCalled();
    const command = emitted.mock.calls[0][0]!;
    results.set({
      [receivingKey({ ...command.correlation, operationId: 'other' })]: {
        correlation: { ...command.correlation, operationId: 'other' },
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    expect(closed).not.toHaveBeenCalled();
    await TestBed.inject(ReceivingDraftService).settle(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      'rejected',
    );
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'rejected',
        errorMessage: 'purchaseOrders.receiving.invalidRow',
        rowErrors: [
          {
            clientLineId: command.payload.lines[0].clientLineId,
            field: 'quantity',
            code: 'Quantity',
            messageKey: 'purchaseOrders.receiving.invalidQuantity',
          },
        ],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(c['loading']()).toBe(false));
    expect(c['form'].enabled).toBe(true);
    expect(c.visible).toBe(true);
    expect(c['errorsFor'](command.payload.lines[0].clientLineId!)).toHaveLength(1);
    await c['submit']();
    const next = emitted.mock.calls[1][0]!;
    await TestBed.inject(ReceivingDraftService).settle(
      next.correlation.scope,
      'po-1',
      next.correlation.operationId,
      'confirmed',
    );
    results.set({
      [receivingKey(next.correlation)]: {
        correlation: next.correlation,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(closed).toHaveBeenCalled());
  });

  it('reopens an uncertain final receipt on a Received PO and retries the byte-equivalent frozen payload', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    c['lines'].at(0).patchValue({
      quantity: 3,
      batchNumber: 'final',
      taxIncluded: true,
      purchaseTaxIncluded: false,
    });
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    fixture.destroy();
    fixture = TestBed.createComponent(ReceivePurchaseOrderDialogComponent);
    fixture.componentRef.setInput('order', {
      ...order,
      status: 'Received',
      lines: [{ ...order.lines[0], remainingQuantity: 0, unitCost: 999 }],
    });
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const restored = fixture.componentInstance;
    await vi.waitFor(() => expect(restored['loading']()).toBe(false));
    expect(restored['status']()).toBe('unknown');
    expect(restored['form'].disabled).toBe(true);
    const replay = vi.spyOn(restored.receive, 'emit');
    restored['retryReceipt']();
    expect(JSON.stringify(replay.mock.calls[0][0]!.payload)).toBe(JSON.stringify(command.payload));
    expect(replay.mock.calls[0][0]!.correlation.operationId).toBe(command.correlation.operationId);
  });

  it('sends nothing when prepare storage fails and keeps the edited fields visible', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    vi.spyOn(TestBed.inject(ReceivingDraftService), 'prepare').mockRejectedValueOnce(
      new Error('quota'),
    );
    c['form'].controls.notes.setValue('keep this');
    await c['submit']();
    expect(emitted).not.toHaveBeenCalled();
    expect(c['form'].controls.notes.value).toBe('keep this');
    expect(c['errorMessage']()).toBe('purchaseOrders.receiving.storageError');
    expect(c.visible).toBe(true);
  });

  it('flushes drafts on deliberate leave, blocks in-flight work and detaches late responses', async () => {
    const c = fixture.componentInstance;
    const context = TestBed.inject(ShopContextService);
    c['form'].controls.notes.setValue('saved before switch');
    expect(await context.prepareToLeave()).toEqual({ ok: true });
    expect(
      (await TestBed.inject(ReceivingDraftService).load(context.scope()!, 'po-1'))!.draft.notes,
    ).toBe('saved before switch');
    const mutation = context.beginMutation();
    expect(await context.prepareToLeave()).toEqual({ ok: false, reason: 'mutation-in-flight' });
    mutation.release();
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    context.transition(null, 'sign-out', () => {});
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    expect(c['form'].disabled).toBe(true);
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po-1'))!
        .pending!.operationId,
    ).toBe(command.correlation.operationId);
  });

  it('ignores late tax defaults after manual edits or row removal', async () => {
    const c = fixture.componentInstance;
    let finish!: (value: {
      hsnCodes: string[];
      taxScenarios: { condition: string; taxPercentage: string }[];
    }) => void;
    batchDefaults.lookupHsn.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    c['lookupTaxForLine'](0);
    c['lines'].at(0).controls.taxRatePercent.setValue(7);
    c['lines'].at(0).controls.taxRatePercent.markAsDirty();
    finish({ hsnCodes: ['0401'], taxScenarios: [{ condition: 'default', taxPercentage: '18%' }] });
    await fixture.whenStable();
    expect(c['lines'].at(0).controls.taxRatePercent.value).toBe(7);
    c['addLine']();
    batchDefaults.lookupHsn.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    c['lookupTaxForLine'](0);
    c['removeLine'](0);
    c['lines'].at(0).controls.taxRatePercent.setValue(9);
    finish({ hsnCodes: ['0401'], taxScenarios: [{ condition: 'default', taxPercentage: '18%' }] });
    await fixture.whenStable();
    expect(c['lines'].at(0).controls.taxRatePercent.value).toBe(9);
  });
  it('pending_receiving_escape_preserves_intent and keeps an active write open', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    expect(c['status']()).toBe('sending');
    const dialog = fixture.nativeElement.querySelector('[role="dialog"]') as HTMLElement;
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await c['hide']();
    expect(c.visible).toBe(true);
    const command = emitted.mock.calls[0][0]!;
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po-1'))!
        .pending!.payload,
    ).toEqual(command.payload);
  });
  it('allows Escape after an unknown outcome and restores its exact durable intent on reopen', async () => {
    const c = fixture.componentInstance;
    const storage = TestBed.inject(ReceivingDraftService);
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settleFailure(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      new Error('lost response'),
      false,
    );
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'unknown',
        errorMessage: 'purchaseOrders.receiving.unknown',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(c['loading']()).toBe(false));
    fixture.nativeElement
      .querySelector('[role="dialog"]')
      .dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
    await vi.waitFor(() => expect(c.visible).toBe(false));
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await vi.waitFor(() => expect(c['loading']()).toBe(false));
    expect(c['status']()).toBe('unknown');
    expect(c['form'].disabled).toBe(true);
    const replay = vi.spyOn(c.receive, 'emit');
    c['retryReceipt']();
    expect(replay.mock.calls.at(-1)?.[0]?.payload).toEqual(command.payload);
    expect(replay.mock.calls.at(-1)?.[0]?.correlation.operationId).toBe(
      command.correlation.operationId,
    );
  });
  it('Escape awaits the existing draft flush and refuses closure when storage fails', async () => {
    const c = fixture.componentInstance;
    let rejectSave!: (reason: unknown) => void;
    const save = vi.spyOn(TestBed.inject(ReceivingDraftService), 'save').mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectSave = reject;
        }),
    );
    fixture.nativeElement
      .querySelector('[role="dialog"]')
      .dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
    await vi.waitFor(() => expect(save).toHaveBeenCalled());
    expect(c.visible).toBe(true);
    rejectSave(new Error('storage unavailable'));
    await vi.waitFor(() =>
      expect(c['errorMessage']()).toBe('purchaseOrders.receiving.storageError'),
    );
    expect(c.visible).toBe(true);
  });
  it('does not let delayed confirmation cleanup close or mutate a reopened dialog lifetime', async () => {
    const c = fixture.componentInstance;
    const storage = TestBed.inject(ReceivingDraftService);
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settle(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      'confirmed',
    );
    const tombstone = (await storage.load(command.correlation.scope, 'po-1'))!;
    let finish!: (value: typeof tombstone) => void;
    vi.spyOn(storage, 'load').mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(finish).toBeDefined());
    await c['hide']();
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await vi.waitFor(() => expect(c['loading']()).toBe(false));
    const before = c['form'].getRawValue();
    const closed = vi.spyOn(c.closed, 'emit');
    finish(tombstone);
    await new Promise((resolve) => setTimeout(resolve, 25));
    fixture.detectChanges();
    expect(c.visible).toBe(true);
    expect(closed).not.toHaveBeenCalled();
    expect(c['form'].getRawValue()).toEqual(before);
  });

  it.each(['pending', 'mismatched', 'missing', 'read-failed'])(
    'does not close or clear work for an unproved durable confirmation (%s)',
    async (mode) => {
      const c = fixture.componentInstance;
      const emitted = vi.spyOn(c.receive, 'emit');
      const closed = vi.spyOn(c.closed, 'emit');
      await c['submit']();
      const command = emitted.mock.calls[0][0]!;
      const storage = TestBed.inject(ReceivingDraftService);
      const stored = (await storage.load(command.correlation.scope, 'po-1'))!;
      if (mode === 'mismatched')
        vi.spyOn(storage, 'load').mockResolvedValueOnce({
          ...stored,
          pending: null,
          confirmedOperationId: 'another-operation',
        });
      if (mode === 'missing') vi.spyOn(storage, 'load').mockResolvedValueOnce(null);
      if (mode === 'read-failed')
        vi.spyOn(storage, 'load').mockRejectedValueOnce(new Error('storage read failed'));
      results.set({
        [receivingKey(command.correlation)]: {
          correlation: command.correlation,
          status: 'confirmed',
          errorMessage: '',
          rowErrors: [],
        },
      });
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 25));
      expect(c.visible).toBe(true);
      expect(closed).not.toHaveBeenCalled();
      expect(
        (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po-1'))!
          .pending!.operationId,
      ).toBe(command.correlation.operationId);
    },
  );

  it('closes after exact confirmation even if another tab already cleaned its tombstone', async () => {
    const c = fixture.componentInstance;
    const storage = TestBed.inject(ReceivingDraftService);
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settle(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      'confirmed',
    );
    await new ReceivingDraftService().clearConfirmed(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
    );
    const closed = vi.spyOn(c.closed, 'emit');
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(closed).toHaveBeenCalledOnce());
    expect(
      await storage.isConfirmed(command.correlation.scope, 'po-1', command.correlation.operationId),
    ).toBe(true);
  });

  it('keeps exact durable confirmation when cleanup fails, while safely closing its own dialog', async () => {
    const c = fixture.componentInstance;
    const storage = TestBed.inject(ReceivingDraftService);
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settle(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      'confirmed',
    );
    vi.spyOn(storage, 'clearConfirmed').mockRejectedValueOnce(new Error('cleanup failed'));
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(c.visible).toBe(false));
    expect((await storage.load(command.correlation.scope, 'po-1'))!.confirmedOperationId).toBe(
      command.correlation.operationId,
    );
    await expect(
      storage.prepare(
        command.correlation.scope,
        'po-1',
        { referenceNumber: '', notes: '', receivedAt: null, lines: [] },
        0,
      ),
    ).rejects.toThrow();
  });

  it('keeps the UI frozen after a retry miss and preserves edited B when unrelated A confirms', async () => {
    const c = fixture.componentInstance;
    const emitted = vi.spyOn(c.receive, 'emit');
    const storage = TestBed.inject(ReceivingDraftService);
    c['form'].controls.notes.setValue('B draft remains');
    const before = c['form'].getRawValue();
    const unrelated = {
      scope: TestBed.inject(ShopContextService).scope()!,
      purchaseOrderId: 'po-A',
      operationId: 'op-A',
    };
    results.set({
      [receivingKey(unrelated)]: {
        correlation: unrelated,
        status: 'confirmed',
        errorMessage: '',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    expect(c['form'].getRawValue()).toEqual(before);
    expect(c.visible).toBe(true);
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settleFailure(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      new Error('lost response'),
      false,
    );
    await storage.settleFailure(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      new HttpErrorResponse({ status: 404 }),
      true,
    );
    results.set({
      [receivingKey(command.correlation)]: {
        correlation: command.correlation,
        status: 'unknown',
        errorMessage: 'purchaseOrders.receiving.unknown',
        rowErrors: [],
      },
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(c['loading']()).toBe(false));
    expect(c['form'].disabled).toBe(true);
    expect(c.visible).toBe(true);
    expect((await storage.load(command.correlation.scope, 'po-1'))!.pending!.payload).toEqual(
      command.payload,
    );
  });

  it('safely cleans an exact restored confirmation on manual close so later receipts remain possible', async () => {
    const c = fixture.componentInstance;
    const storage = TestBed.inject(ReceivingDraftService);
    const emitted = vi.spyOn(c.receive, 'emit');
    await c['submit']();
    const command = emitted.mock.calls[0][0]!;
    await storage.settle(
      command.correlation.scope,
      'po-1',
      command.correlation.operationId,
      'confirmed',
    );
    fixture.destroy();
    fixture = TestBed.createComponent(ReceivePurchaseOrderDialogComponent);
    fixture.componentRef.setInput('order', order);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const restored = fixture.componentInstance;
    await vi.waitFor(() => expect(restored['loading']()).toBe(false));
    expect(restored['status']()).toBe('confirmed');
    await restored['hide']();
    expect(await storage.load(command.correlation.scope, 'po-1')).toBeNull();
    expect(
      await storage.isConfirmed(command.correlation.scope, 'po-1', command.correlation.operationId),
    ).toBe(true);
  });
});
