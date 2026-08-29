import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from './inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BatchRowFormStateService } from './batch-row-form-state.service';

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

  const suppliersFacade = {
    suppliers: signal<any[]>([]),
    load: vi.fn(),
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
        { provide: SuppliersFacade, useValue: suppliersFacade },
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
});
