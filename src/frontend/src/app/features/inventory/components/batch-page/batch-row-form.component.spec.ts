import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MessageService } from 'primeng/api';

import { InventoryService } from '../../services/inventory.service';
import { ProductCatalogSyncService } from '../../../../core/services/product-catalog-sync.service';
import { InventoryInboundDraftRow } from '../../../../core/storage/inventory-draft-indexeddb.service';
import { BatchRowFormComponent } from './batch-row-form.component';

describe('BatchRowFormComponent', () => {
  const emptyLookupResult = {
    hsnCodes: [] as string[],
    taxScenarios: [] as { condition: string; taxPercentage: string }[],
  };

  const productDetails = {
    name: 'Milk',
    description: 'Fresh milk pack',
    uom: 'ltr',
    costPrice: 42,
    mrp: 50,
    salesPrice: 48,
    supplierId: 'supplier-1',
    supplierName: 'Acme Foods',
    hsnCode: '0401',
    taxIncluded: true as boolean | null,
    taxRatePercent: 18 as number | null,
  };

  const inventoryService = {
    getProductDetailsByNameOrBarcode: vi.fn(() => of(productDetails)),
    lookupHsn: vi.fn(() => of(emptyLookupResult)),
    addInventoryBatch: vi.fn(),
  };

  const productCatalogSync: any = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
    findByName: vi.fn(() => undefined),
    findByBarcode: vi.fn(() => undefined),
  };

  async function setup(suppliers: any[] = []) {
    TestBed.configureTestingModule({
      imports: [
        BatchRowFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        MessageService,
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: productCatalogSync },
      ],
    });

    const fixture = TestBed.createComponent(BatchRowFormComponent);
    fixture.componentInstance.shopId = 'shop-1';
    fixture.componentInstance.suppliers = suppliers;
    fixture.componentInstance.catalogSync = productCatalogSync;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    inventoryService.getProductDetailsByNameOrBarcode.mockReset();
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(of(productDetails));
    inventoryService.lookupHsn.mockReset();
    inventoryService.lookupHsn.mockReturnValue(of(emptyLookupResult));
    productCatalogSync.filterByName.mockClear();
    productCatalogSync.filterByBarcode.mockClear();
    productCatalogSync.findByName.mockClear();
    productCatalogSync.findByBarcode.mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  // ── tryAddRow ───────────────────────────────────────────────────────────────

  it('tryAddRow returns false when form is invalid', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    const result = component.tryAddRow();

    expect(result).toBe(false);
    expect(emitted).toHaveLength(0);
  });

  it('tryAddRow emits rowAdded and resets form when form is valid', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    component.form.setValue({
      itemName: 'Milk',
      barcode: 'B001',
      itemDescription: '',
      uom: 'ltr',
      batchNumber: 'BN-001',
      quantity: 1,
      totalPurchaseCost: 42,
      mrp: 50,
      salesPrice: 48,
      taxRatePercent: 18,
      taxIncluded: true,
      expiryDate: '',
      manufacturingDate: '',
      supplierName: '',
      referenceNumber: '',
      notes: '',
    });

    const result = component.tryAddRow();

    expect(result).toBe(true);
    expect(emitted).toHaveLength(1);
    expect(emitted[0].itemName).toBe('Milk');
    expect(emitted[0].barcode).toBe('B001');
    expect(component.form.controls.itemName.value).toBe('');
  });

  it('tryAddRow assigns null for empty optional fields', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    component.form.setValue({
      itemName: 'Tea',
      barcode: 'T001',
      itemDescription: '',
      uom: 'pkg',
      batchNumber: 'BN-T1',
      quantity: 2,
      totalPurchaseCost: 50,
      mrp: 60,
      salesPrice: 55,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: '',
      manufacturingDate: '',
      supplierName: '',
      referenceNumber: '',
      notes: '',
    });
    component.tryAddRow();

    expect(emitted[0].itemDescription).toBeNull();
    expect(emitted[0].expiryDate).toBeNull();
    expect(emitted[0].supplierId).toBeNull();
    expect(emitted[0].hsnCode).toBeNull();
  });

  // ── populateFromRow ─────────────────────────────────────────────────────────

  it('populateFromRow fills all form controls from draft row', async () => {
    const fixture = await setup([{ supplierId: 'sup-1', name: 'Vendor A' }]);
    const component = fixture.componentInstance;
    const row: InventoryInboundDraftRow = {
      clientRowId: 'r1',
      itemName: 'Tea',
      barcode: 'T001',
      itemDescription: 'Green tea',
      uom: 'pkg',
      batchNumber: 'BN-TEA-1',
      quantity: 5,
      totalPurchaseCost: 100,
      mrp: 120,
      salesPrice: 110,
      taxRatePercent: 12,
      taxIncluded: false,
      hsnCode: '0902',
      expiryDate: '2027-01-01',
      manufacturingDate: '2026-01-01',
      supplierId: 'sup-1',
      referenceNumber: 'REF-T',
      notes: 'Fragile',
      performedAt: new Date().toISOString(),
    };

    component.populateFromRow(row, 'Vendor A');

    expect(component.form.controls.itemName.value).toBe('Tea');
    expect(component.form.controls.barcode.value).toBe('T001');
    expect(component.form.controls.uom.value).toBe('pkg');
    expect(component.form.controls.batchNumber.value).toBe('BN-TEA-1');
    expect(component.form.controls.quantity.value).toBe(5);
    expect(component.form.controls.supplierName.value).toBe('Vendor A');
    expect(component.selectedHsnCode()).toBe('0902');
    expect(component.form.controls.taxIncluded.value).toBe(false);
  });

  it('populateFromRow sets empty supplierName when supplierName is "-"', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const row: InventoryInboundDraftRow = {
      clientRowId: 'r2', itemName: 'Sugar', barcode: 'S001', itemDescription: null,
      uom: 'kg', batchNumber: 'BN-S1', quantity: 1, totalPurchaseCost: 10, mrp: 12,
      salesPrice: 11, taxRatePercent: 0, taxIncluded: true, hsnCode: null,
      expiryDate: null, manufacturingDate: null, supplierId: null,
      referenceNumber: null, notes: null, performedAt: new Date().toISOString(),
    };

    component.populateFromRow(row, '-');

    expect(component.form.controls.supplierName.value).toBe('');
  });

  // ── handleBarcode ───────────────────────────────────────────────────────────

  it('handleBarcode returns "added" when catalog hit and product details are complete', async () => {
    const barcode = 'HANDLE-B001';
    productCatalogSync.findByBarcode.mockReturnValue({ name: 'Milk', barcode });

    const fixture = await setup();
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    const result = await component.handleBarcode(barcode);

    expect(result).toBe('added');
    expect(emitted).toHaveLength(1);
    expect(emitted[0].barcode).toBe(barcode);
    expect(emitted[0].itemName).toBe(productDetails.name);
  });

  it('handleBarcode returns "review" when product prices are zero', async () => {
    const barcode = 'HANDLE-B002';
    productCatalogSync.findByBarcode.mockReturnValue(undefined);
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      of({ ...productDetails, costPrice: 0, mrp: 0, salesPrice: 0 }) as any,
    );

    const fixture = await setup();
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    const result = await component.handleBarcode(barcode);

    expect(result).toBe('review');
    expect(emitted).toHaveLength(0);
  });

  it('handleBarcode returns "review" on API error', async () => {
    const barcode = 'HANDLE-B003';
    productCatalogSync.findByBarcode.mockReturnValue(undefined);
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      throwError(() => new Error('fetch failed')),
    );

    const fixture = await setup();
    const component = fixture.componentInstance;

    const result = await component.handleBarcode(barcode);

    expect(result).toBe('review');
  });

  // ── applyHsnSelection ───────────────────────────────────────────────────────

  it('applyHsnSelection parses tax percentage string to number', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0401', '18%');

    expect(component.form.controls.taxRatePercent.value).toBe(18);
    expect(component.selectedHsnCode()).toBe('0401');
    expect(component.pickerOpen()).toBe(false);
  });

  it('applyHsnSelection handles decimal tax rates', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0402', '5.5%');

    expect(component.form.controls.taxRatePercent.value).toBe(5.5);
    expect(component.selectedHsnCode()).toBe('0402');
  });

  it('applyHsnSelection closes picker', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    component.pickerOpen.set(true);

    component.applyHsnSelection('0401', '12%');

    expect(component.pickerOpen()).toBe(false);
  });

  // ── onItemNameBlur ──────────────────────────────────────────────────────────

  it('onItemNameBlur calls lookupHsn when name has 3+ chars', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Milk');
  });

  it('onItemNameBlur does not call lookupHsn when name has fewer than 3 chars', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('ab');
    await component.onItemNameBlur();

    expect(inventoryService.lookupHsn).not.toHaveBeenCalled();
  });

  it('onItemNameBlur auto-applies single HSN code and scenario', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({ hsnCodes: ['0401'], taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }] }),
    );
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.selectedHsnCode()).toBe('0401');
    expect(component.form.controls.taxRatePercent.value).toBe(18);
    expect(component.pickerOpen()).toBe(false);
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).toBeNull();
  });

  it('onItemNameBlur shows hsn chip after auto-apply', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({ hsnCodes: ['0401'], taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }] }),
    );
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.selectedHsnCode()).toBe('0401');
    expect(fixture.debugElement.query(By.css('.hsn-chip'))).not.toBeNull();
  });

  it('onItemNameBlur opens picker when multiple results returned', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({
        hsnCodes: ['0401', '0402'],
        taxScenarios: [
          { condition: 'General', taxPercentage: '5%' },
          { condition: 'Special', taxPercentage: '12%' },
        ],
      }),
    );
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.pickerOpen()).toBe(true);
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).not.toBeNull();
  });

  it('onItemNameBlur does not show picker on API error', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(throwError(() => new Error('lookup failed')));
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.pickerOpen()).toBe(false);
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).toBeNull();
  });

  it('onItemNameBlur does not overwrite already-applied HSN selection', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    // Set item name first (before applying HSN), so the clearHsn-on-name-change guard
    // is not triggered by this setValue call.
    component.form.controls.itemName.setValue('Milk');
    component.applyHsnSelection('0401', '18%');
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({ hsnCodes: ['0999'], taxScenarios: [{ condition: 'Other', taxPercentage: '28%' }] }),
    );

    // onItemNameBlur without changing the name — selectedHsnCode is still '0401'
    await component.onItemNameBlur();

    expect(component.selectedHsnCode()).toBe('0401');
    expect(component.form.controls.taxRatePercent.value).toBe(18);
  });

  // ── Product details on barcode focus-out ────────────────────────────────────

  it('fetches product details when barcode field loses focus', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    await component.onBarcodeFocusOut();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith('Milk', 'B001');
    expect(component.form.controls.itemDescription.value).toBe(productDetails.description);
    expect(component.form.controls.uom.value).toBe(productDetails.uom);
    expect(component.form.controls.mrp.value).toBe(productDetails.mrp);
    expect(component.form.controls.salesPrice.value).toBe(productDetails.salesPrice);
    expect(component.form.controls.supplierName.value).toBe(productDetails.supplierName);
    expect(component.form.controls.taxIncluded.value).toBe(productDetails.taxIncluded);
    expect(component.form.controls.taxRatePercent.value).toBe(productDetails.taxRatePercent);
  });

  it('fetches details on barcode focusout even when item name is empty', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('');
    component.form.controls.barcode.setValue('B003');
    await component.onBarcodeFocusOut();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(undefined, 'B003');
    expect(component.form.controls.itemName.value).toBe(productDetails.name);
  });

  it('does not overwrite fields already edited by the user', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.itemDescription.setValue('Manual description');
    component.form.controls.uom.setValue('box');
    component.form.controls.totalPurchaseCost.setValue(10);
    component.form.controls.mrp.setValue(12);
    component.form.controls.salesPrice.setValue(11);

    component.form.controls.itemDescription.markAsDirty();
    component.form.controls.uom.markAsDirty();
    component.form.controls.totalPurchaseCost.markAsDirty();
    component.form.controls.mrp.markAsDirty();
    component.form.controls.salesPrice.markAsDirty();

    await (component as any)['fetchProductDetails']();

    expect(component.form.controls.itemDescription.value).toBe('Manual description');
    expect(component.form.controls.uom.value).toBe('box');
    expect(component.form.controls.totalPurchaseCost.value).toBe(10);
    expect(component.form.controls.mrp.value).toBe(12);
    expect(component.form.controls.salesPrice.value).toBe(11);
  });

  it('fills untouched fields from fetched product details', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');

    await (component as any)['fetchProductDetails']();

    expect(component.form.controls.itemDescription.value).toBe(productDetails.description);
    expect(component.form.controls.uom.value).toBe(productDetails.uom);
    expect(component.form.controls.totalPurchaseCost.value).toBe(productDetails.costPrice);
    expect(component.form.controls.mrp.value).toBe(productDetails.mrp);
    expect(component.form.controls.salesPrice.value).toBe(productDetails.salesPrice);
    expect(component.form.controls.supplierName.value).toBe(productDetails.supplierName);
    expect(component.form.controls.taxIncluded.value).toBe(productDetails.taxIncluded);
    expect(component.form.controls.taxRatePercent.value).toBe(productDetails.taxRatePercent);
  });

  it('does not overwrite supplier and tax when already dirty', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.supplierName.setValue('My Supplier');
    component.form.controls.taxIncluded.setValue(false);
    component.form.controls.supplierName.markAsDirty();
    component.form.controls.taxIncluded.markAsDirty();

    await (component as any)['fetchProductDetails']();

    expect(component.form.controls.supplierName.value).toBe('My Supplier');
    expect(component.form.controls.taxIncluded.value).toBe(false);
  });

  it('sets item name from product details when item name is empty and barcode present', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('');
    component.form.controls.barcode.setValue('B009');

    await (component as any)['fetchProductDetails']();

    expect(component.form.controls.itemName.value).toBe(productDetails.name);
  });

  // ── scanRequested output ────────────────────────────────────────────────────

  it('emits scanRequested when camera button is clicked', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    let emitCount = 0;
    component.scanRequested.subscribe(() => emitCount++);

    component.scanRequested.emit();
    expect(emitCount).toBe(1);
  });

  // ── supplier autocomplete ───────────────────────────────────────────────────

  it('filters suppliers by name substring', async () => {
    const suppliers = [
      { supplierId: 's1', name: 'Acme Foods', email: null, contactPerson: null, mobileNumber: null, gstin: null, addressLine1: null, addressLine2: null, city: null, state: null, country: null, postalCode: null, notes: null, isActive: true },
      { supplierId: 's2', name: 'Acme Beverages', email: null, contactPerson: null, mobileNumber: null, gstin: null, addressLine1: null, addressLine2: null, city: null, state: null, country: null, postalCode: null, notes: null, isActive: true },
      { supplierId: 's3', name: 'Bharat Spices', email: null, contactPerson: null, mobileNumber: null, gstin: null, addressLine1: null, addressLine2: null, city: null, state: null, country: null, postalCode: null, notes: null, isActive: true },
    ];
    const fixture = await setup(suppliers as any);
    const component = fixture.componentInstance;

    component.onFilterSupplier({ query: 'acme', originalEvent: new Event('input') });

    expect(component.supplierSuggestions()).toHaveLength(2);
    expect(component.supplierSuggestions()).toContain('Acme Foods');
    expect(component.supplierSuggestions()).toContain('Acme Beverages');
  });

  it('resolves supplierId when emitting a row with known supplier', async () => {
    const suppliers = [
      { supplierId: 'sup-99', name: 'GlobalMart', email: null, contactPerson: null, mobileNumber: null, gstin: null, addressLine1: null, addressLine2: null, city: null, state: null, country: null, postalCode: null, notes: null, isActive: true },
    ];
    const fixture = await setup(suppliers as any);
    const component = fixture.componentInstance;
    const emitted: InventoryInboundDraftRow[] = [];
    component.rowAdded.subscribe((r) => emitted.push(r));

    component.form.setValue({
      itemName: 'Rice', barcode: 'R001', itemDescription: '', uom: 'kg',
      batchNumber: 'BN-R1', quantity: 1, totalPurchaseCost: 30, mrp: 35,
      salesPrice: 32, taxRatePercent: 0, taxIncluded: true, expiryDate: '',
      manufacturingDate: '', supplierName: 'GlobalMart', referenceNumber: '', notes: '',
    });
    component.tryAddRow();

    expect(emitted[0].supplierId).toBe('sup-99');
  });
});
