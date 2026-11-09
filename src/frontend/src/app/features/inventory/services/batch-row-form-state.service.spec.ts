import { SupplierService } from '../../suppliers/services/supplier.service';
import { supplierOptionLabel } from '../../../shared/models/party-directory.models';
import { ExpiryPolicyService } from '../../expiry/services/expiry-policy.service';
import { EMPTY, Subject } from 'rxjs';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from './inventory.service';
import type { ProductDetailsDto } from './inventory.models';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BatchRowFormStateService } from './batch-row-form-state.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';

describe('BatchRowFormStateService', () => {
  const inventoryService = {
    getProductDetailsByNameOrBarcode: vi.fn(),
    lookupHsn: vi.fn(),
    generateItemBarcode: vi.fn(() => of({ barcode: 'GEN-SVC-001' })),
  };

  const catalogSync = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
    findByName: vi.fn(() => undefined),
    findByBarcode: vi.fn(() => undefined),
  };

  const lateSupplier = {
    supplierId: 'late-2',
    name: 'Same',
    city: 'B',
    contactPersonPhone: '123',
    isActive: true,
  };
  const supplierService = {
    getDirectory: vi.fn(() =>
      of({ items: [{ ...lateSupplier, supplierId: 'late-1' }, lateSupplier] }),
    ),
    getSupplier: vi.fn(() => of(lateSupplier)),
  };

  const suppliersFacade = {
    suppliers: signal<any[]>([]),
    load: vi.fn(),
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        {
          provide: ExpiryPolicyService,
          useValue: {
            read: () =>
              of({
                effectivePolicy: {
                  version: 1,
                  mode: 'Ignore',
                  expiryRequired: false,
                  sellThroughExpiryDate: true,
                  warningDays: 7,
                  overrideAllowed: false,
                },
              }),
            changed$: EMPTY,
          },
        },
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
        { provide: SuppliersFacade, useValue: suppliersFacade },
        { provide: SupplierService, useValue: supplierService },
      ],
    });

    return TestBed.inject(BatchRowFormStateService);
  }

  beforeEach(() => {
    inventoryService.getProductDetailsByNameOrBarcode.mockReset();
    inventoryService.lookupHsn.mockReset();
    inventoryService.generateItemBarcode.mockReset();
    inventoryService.generateItemBarcode.mockReturnValue(of({ barcode: 'GEN-SVC-001' }));
    catalogSync.filterByName.mockClear();
    catalogSync.filterByBarcode.mockClear();
    catalogSync.findByName.mockClear();
    catalogSync.findByBarcode.mockClear();
    suppliersFacade.suppliers.set([]);
    suppliersFacade.load.mockClear();
  });

  it('preserves the supplier ID from product defaults without a complete directory', async () => {
    const state = setup();
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      of({
        name: 'Rice',
        description: '',
        uom: 'PCS',
        costPrice: 1,
        mrp: 2,
        salesPrice: 2,
        supplierId: 'late-2',
        supplierName: 'Same',
        hsnCode: null,
        taxIncluded: false,
        taxRatePercent: 0,
        purchaseTaxIncluded: false,
      }),
    );
    state.form.patchValue({ barcode: 'B', itemName: 'Rice', batchNumber: 'Lot' });
    await state.fetchProductDetails();
    expect(state.buildDraftRow()?.supplierId).toBe('late-2');
  });

  it('never infers a supplier ID from a bare name when a duplicate is beyond the bounded page', () => {
    vi.useFakeTimers();
    const state = setup();
    const first = { ...lateSupplier, supplierId: 'acme-a', name: 'Acme', city: 'City A' };
    const hidden = { ...lateSupplier, supplierId: 'acme-b', name: 'Acme', city: 'City B' };
    const all = [
      ...Array.from({ length: 19 }, (_, index) => ({
        ...lateSupplier,
        supplierId: `prefix-${index}`,
        name: `0${index} Acme match`,
      })),
      first,
      hidden,
    ];
    supplierService.getDirectory.mockReturnValueOnce(
      of({ items: all.slice(0, 20), totalCount: 21 }),
    );
    try {
      state.form.patchValue({
        itemName: 'Rice',
        barcode: 'B',
        batchNumber: 'Lot',
        quantity: 1,
        totalPurchaseCost: 1,
        mrp: 2,
        salesPrice: 2,
      });
      state.onFilterSupplier({ query: 'Acme', originalEvent: new Event('input') });
      vi.advanceTimersByTime(250);
      expect(state.suppliers()).toHaveLength(20);
      state.form.controls.supplierName.setValue('Acme');
      expect(state.selectedSupplier()).toBeNull();
      expect(state.buildDraftRow()?.supplierId).toBeNull();
      state.onSupplierSelected('Acme');
      expect(state.selectedSupplier()).toBeNull();
      state.onSupplierSelected(supplierOptionLabel(first as any));
      expect(state.buildDraftRow()?.supplierId).toBe('acme-a');
      supplierService.getDirectory.mockReturnValueOnce(of({ items: [hidden], totalCount: 1 }));
      state.onFilterSupplier({ query: 'City B', originalEvent: new Event('input') });
      vi.advanceTimersByTime(250);
      expect(state.buildDraftRow()?.supplierId).toBe('acme-a');
      state.onSupplierSelected(supplierOptionLabel(hidden as any));
      expect(state.buildDraftRow()?.supplierId).toBe('acme-b');
      state.form.controls.supplierName.setValue('Acme');
      expect(state.buildDraftRow()?.supplierId).toBeNull();
      expect(state.form.controls.supplierName.value).toBe('Acme');
      state.form.controls.supplierName.setValue('');
      expect(state.buildDraftRow()?.supplierId).toBeNull();
    } finally {
      TestBed.resetTestingModule();
      vi.useRealTimers();
    }
  });

  it('selects a remote duplicate-name supplier by ID and retains it while searching', () => {
    vi.useFakeTimers();
    const state = setup();
    state.onFilterSupplier({ query: 'Same', originalEvent: new Event('input') });
    vi.advanceTimersByTime(250);
    expect(supplierService.getDirectory).toHaveBeenCalledWith(
      expect.objectContaining({ search: 'Same', pageSize: 20 }),
    );
    state.onSupplierSelected(supplierOptionLabel(lateSupplier as any));
    state.onFilterSupplier({ query: 'Other', originalEvent: new Event('input') });
    vi.advanceTimersByTime(250);
    state.form.patchValue({
      itemName: 'Rice',
      barcode: 'B',
      quantity: 1,
      batchNumber: 'Lot',
      totalPurchaseCost: 1,
      mrp: 2,
      salesPrice: 2,
    });
    expect(state.buildDraftRow()?.supplierId).toBe('late-2');
    vi.useRealTimers();
  });

  it('refuses manufacture after expiry before building a stock intake row', () => {
    const state = setup();
    state.form.patchValue({
      itemName: 'Milk',
      barcode: 'B1',
      uom: 'PCS',
      batchNumber: 'LOT1',
      quantity: 1,
      totalPurchaseCost: 1,
      mrp: 2,
      salesPrice: 2,
      purchaseTaxIncluded: false,
      manufacturingDate: '2026-11-10',
      expiryDate: '2026-11-09',
    });
    expect(state.buildDraftRow()).toBeNull();
  });

  it('fetches product details and patches untouched fields', async () => {
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValueOnce(
      of({
        name: 'Milk',
        description: 'Fresh milk pack',
        uom: 'ltr',
        costPrice: 42,
        mrp: 50,
        salesPrice: 48,
        supplierId: null,
        supplierName: 'Acme Foods',
        hsnCode: '0401',
        taxIncluded: true,
        taxRatePercent: 18,
      }),
    );

    const state = setup();
    state.form.controls.itemName.setValue('Milk');
    state.form.controls.barcode.setValue('B001');

    const result = await state.fetchProductDetails();

    expect(result.patched).toBe(true);
    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith('Milk', 'B001');
    expect(state.form.controls.itemDescription.value).toBe('Fresh milk pack');
    expect(state.form.controls.uom.value).toBe('ltr');
    expect(state.selectedHsnCode()).toBe('0401');
  });

  it('looks up HSN and auto-applies a single option', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({
        hsnCodes: ['0401'],
        taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
      }),
    );

    const state = setup();
    state.form.controls.itemName.setValue('Milk');

    await state.onItemNameBlur();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Milk');
    expect(state.selectedHsnCode()).toBe('0401');
    expect(state.form.controls.taxRatePercent.value).toBe(18);
  });

  it('builds a draft row with the selected HSN code', () => {
    const state = setup();
    state.form.controls.itemName.setValue('Milk');
    state.form.controls.barcode.setValue('B001');
    state.form.controls.uom.setValue('ltr');
    state.form.controls.batchNumber.setValue('BN-1');
    state.form.controls.totalPurchaseCost.setValue(42);
    state.form.controls.mrp.setValue(50);
    state.form.controls.salesPrice.setValue(48);
    state.selectedHsnCode.set('0401');

    const row = state.buildDraftRow();

    expect(row?.hsnCode).toBe('0401');
    expect(row?.barcode).toBe('B001');
  });

  it('clears HSN state when requested', () => {
    const state = setup();
    state.selectedHsnCode.set('0401');
    state.pickerOpen.set(true);

    state.clearHsnSelection();

    expect(state.selectedHsnCode()).toBeNull();
    expect(state.pickerOpen()).toBe(false);
  });

  it('defaults UOM to PCS and resets back to PCS', () => {
    const state = setup();
    expect(state.form.controls.uom.value).toBe('PCS');

    state.form.controls.uom.setValue('ltr');
    state.resetForm();

    expect(state.form.controls.uom.value).toBe('PCS');
  });

  it('can auto-add scanned row without purchase cost', () => {
    const state = setup();
    state.form.controls.itemName.setValue('Milk');
    state.form.controls.barcode.setValue('B001');
    state.form.controls.uom.setValue('PCS');
    state.form.controls.totalPurchaseCost.setValue(0);
    state.form.controls.mrp.setValue(50);
    state.form.controls.salesPrice.setValue(48);

    expect(state.canAutoAddScannedRow()).toBe(true);
  });

  it('generateBarcode patches barcode when field is empty', async () => {
    const state = setup();
    state.form.controls.barcode.setValue('');

    const result = await state.generateBarcode();

    expect(result).toEqual({ needsConfirm: false, barcode: 'GEN-SVC-001' });
    expect(state.form.controls.barcode.value).toBe('GEN-SVC-001');
    expect(state.barcodeGenerateError()).toBe('');
  });

  it('generateBarcode returns needsConfirm when field is non-empty', async () => {
    const state = setup();
    state.form.controls.barcode.setValue('EXISTING');

    const result = await state.generateBarcode();

    expect(result).toEqual({ needsConfirm: true, barcode: 'GEN-SVC-001' });
    expect(state.form.controls.barcode.value).toBe('EXISTING');
  });

  it('patchGeneratedBarcode updates barcode in form', () => {
    const state = setup();
    state.form.controls.barcode.setValue('OLD');

    state.patchGeneratedBarcode('GEN-SVC-001');

    expect(state.form.controls.barcode.value).toBe('GEN-SVC-001');
  });

  it('generateBarcode returns error on service failure', async () => {
    inventoryService.generateItemBarcode.mockReturnValue(throwError(() => new Error('fail')));
    const state = setup();

    const result = await state.generateBarcode();

    expect(result).toEqual({ error: true });
    expect(state.barcodeGenerateError()).toBe('inventory.generateBarcodeError');
  });

  it('requires MRP and sales price before auto-adding scanned row', () => {
    const state = setup();
    state.form.controls.itemName.setValue('Milk');
    state.form.controls.barcode.setValue('B001');
    state.form.controls.uom.setValue('PCS');
    state.form.controls.totalPurchaseCost.setValue(0);

    state.form.controls.mrp.setValue(0);
    state.form.controls.salesPrice.setValue(48);
    expect(state.canAutoAddScannedRow()).toBe(false);

    state.form.controls.mrp.setValue(50);
    state.form.controls.salesPrice.setValue(0);
    expect(state.canAutoAddScannedRow()).toBe(false);
  });

  it('matches inbound inventory text and pricing constraints', () => {
    const state = setup();
    state.form.patchValue({
      itemName: 'N'.repeat(180),
      barcode: 'B'.repeat(128),
      itemDescription: 'D'.repeat(1000),
      uom: 'U'.repeat(32),
      batchNumber: 'BATCH-1',
      mrp: 100,
      salesPrice: 101,
      taxRatePercent: 101,
    });

    expect(state.form.controls.barcode.valid).toBe(true);
    expect(state.form.controls.itemDescription.valid).toBe(true);
    expect(state.form.hasError('salesPriceExceedsMrp')).toBe(true);
    expect(state.form.controls.taxRatePercent.hasError('max')).toBe(true);
  });

  describe('purchaseAndSalesTaxModesRemainIndependent', () => {
    function productDetails(): ProductDetailsDto {
      return {
        name: 'Milk',
        description: 'Fresh milk pack',
        uom: 'ltr',
        costPrice: 42,
        mrp: 50,
        salesPrice: 48,
        supplierId: null,
        supplierName: null,
        hsnCode: null,
        taxIncluded: true,
        taxRatePercent: 18,
        purchaseTaxIncluded: true,
      };
    }

    function draftRow(overrides: Partial<InventoryInboundDraftRow> = {}): InventoryInboundDraftRow {
      return {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'PCS',
        batchNumber: 'BN-1',
        quantity: 2,
        totalPurchaseCost: 80,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 5,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        purchaseTaxModeConfirmed: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: null,
        ...overrides,
      };
    }

    it.each(['', 'Changed supplier'])(
      'keeps explicit supplier input %j when a draft ID response arrives late',
      (input) => {
        const state = setup();
        const pending = new Subject<any>();
        supplierService.getSupplier.mockReturnValueOnce(pending);
        state.loadDraftRow(draftRow({ supplierId: 'late-1' }));
        state.form.controls.supplierName.setValue(input);
        pending.next({ ...lateSupplier, supplierId: 'late-1' });
        expect(state.form.controls.supplierName.value).toBe(input);
        expect(state.buildDraftRow()?.supplierId).toBeNull();
      },
    );

    it('does not let a delayed draft ID restore replace a newer supplier selection', () => {
      vi.useFakeTimers();
      const state = setup();
      const pending = new Subject<any>();
      supplierService.getSupplier.mockReturnValueOnce(pending);
      state.loadDraftRow(draftRow({ supplierId: 'late-1' }));
      state.onFilterSupplier({ query: 'Same', originalEvent: new Event('input') });
      vi.advanceTimersByTime(250);
      state.onSupplierSelected(supplierOptionLabel(lateSupplier as any));
      pending.next({ ...lateSupplier, supplierId: 'late-1' });
      expect(state.buildDraftRow()?.supplierId).toBe('late-2');
      vi.useRealTimers();
    });

    it.each([
      { purchaseTaxIncluded: true, taxIncluded: true },
      { purchaseTaxIncluded: true, taxIncluded: false },
      { purchaseTaxIncluded: false, taxIncluded: true },
      { purchaseTaxIncluded: false, taxIncluded: false },
    ])(
      'keeps purchase $purchaseTaxIncluded and sales $taxIncluded through load and build',
      ({ purchaseTaxIncluded, taxIncluded }) => {
        const state = setup();

        state.loadDraftRow(draftRow({ purchaseTaxIncluded, taxIncluded }));
        const row = state.buildDraftRow();

        expect(row?.purchaseTaxIncluded).toBe(purchaseTaxIncluded);
        expect(row?.taxIncluded).toBe(taxIncluded);
      },
    );

    it('requires the purchase mode to be confirmed for a draft stored without it', () => {
      const state = setup();
      const { purchaseTaxIncluded: _omitted, ...oldDraft } = draftRow({ taxIncluded: false });

      state.loadDraftRow(oldDraft);

      expect(state.form.controls.purchaseTaxIncluded.value).toBeNull();
      expect(state.form.invalid).toBe(true);
      expect(state.buildDraftRow()).toBeNull();

      state.form.controls.purchaseTaxIncluded.setValue(true);

      expect(state.form.valid).toBe(true);
      expect(state.buildDraftRow()?.purchaseTaxIncluded).toBe(true);
      expect(state.buildDraftRow()?.taxIncluded).toBe(false);
    });

    it('requires the purchase mode to be confirmed for a legacy draft whose mode was copied from sales', () => {
      const state = setup();
      // The earlier release stored the sales mode as the purchase mode, without the confirmation marker.
      const { purchaseTaxModeConfirmed: _marker, ...legacyDraft } = draftRow({
        taxIncluded: false,
        purchaseTaxIncluded: false,
      });

      state.loadDraftRow(legacyDraft);

      expect(state.form.controls.purchaseTaxIncluded.value).toBeNull();
      expect(state.buildDraftRow()).toBeNull();

      state.form.controls.purchaseTaxIncluded.setValue(true);
      const rebuilt = state.buildDraftRow();

      expect(rebuilt?.purchaseTaxIncluded).toBe(true);
      expect(rebuilt?.taxIncluded).toBe(false);
      expect(rebuilt?.purchaseTaxModeConfirmed).toBe(true);
    });

    it('fills the purchase mode from the latest batch together with its cost', async () => {
      inventoryService.getProductDetailsByNameOrBarcode.mockReturnValueOnce(
        of({ ...productDetails(), costPrice: 40, taxIncluded: true, purchaseTaxIncluded: false }),
      );
      const state = setup();
      state.form.controls.barcode.setValue('B001');

      await state.fetchProductDetails();

      expect(state.form.controls.totalPurchaseCost.value).toBe(40);
      expect(state.form.controls.purchaseTaxIncluded.value).toBe(false);
      expect(state.form.controls.taxIncluded.value).toBe(true);
    });

    it('keeps the purchase mode when there is no previous batch or the user already chose it', async () => {
      inventoryService.getProductDetailsByNameOrBarcode.mockReturnValueOnce(
        of({ ...productDetails(), purchaseTaxIncluded: null }),
      );
      const state = setup();
      state.form.controls.barcode.setValue('B001');
      state.form.controls.purchaseTaxIncluded.setValue(false);

      await state.fetchProductDetails();
      expect(state.form.controls.purchaseTaxIncluded.value).toBe(false);

      inventoryService.getProductDetailsByNameOrBarcode.mockReturnValueOnce(
        of({ ...productDetails(), purchaseTaxIncluded: true }),
      );
      state.form.controls.purchaseTaxIncluded.markAsDirty();

      await state.fetchProductDetails();
      expect(state.form.controls.purchaseTaxIncluded.value).toBe(false);
    });

    it('defaults new rows to a purchase cost that includes tax', () => {
      const state = setup();
      state.form.controls.purchaseTaxIncluded.setValue(false);

      state.resetForm();

      expect(state.form.controls.purchaseTaxIncluded.value).toBe(true);
    });
  });
});
