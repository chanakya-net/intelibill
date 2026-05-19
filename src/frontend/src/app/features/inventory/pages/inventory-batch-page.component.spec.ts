import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MessageService } from 'primeng/api';

import { AuthService } from '../../../core/auth/auth.service';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetectorService } from '../../../core/services/barcode-detector.service';
import { CameraStreamService } from '../../../core/services/camera-stream.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryDraftIndexedDbService } from '../../../core/storage/inventory-draft-indexeddb.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { InventoryService } from '../services/inventory.service';
import { InventoryBatchPageComponent } from './inventory-batch-page.component';

describe('InventoryBatchPageComponent', () => {
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
    addInventoryBatch: vi.fn(),
    lookupHsn: vi.fn(() => of(emptyLookupResult)),
  };

  const draftStorage = {
    loadRows: vi.fn(async () => []),
    saveRows: vi.fn(async () => undefined),
    clearRows: vi.fn(async () => undefined),
  };

  const productCatalogSync: any = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
    findByName: vi.fn(() => undefined),
    findByBarcode: vi.fn(() => undefined),
  };

  const suppliersFacade = {
    suppliers: signal<any[]>([]),
    load: vi.fn(),
  };

  const audioService = {
    prime: vi.fn(async () => undefined),
    beep: vi.fn(async () => undefined),
  };

  const barcodeDetectorService = {
    preferredEngineLabel: 'Native detector',
    start: vi.fn(async () => vi.fn()),
  };

  const cameraStreamService = {
    startPreferredCamera: vi.fn(),
    attachToVideo: vi.fn(),
    detachVideo: vi.fn(),
    stopCurrentStream: vi.fn(),
  };

  const authService = {
    session: signal({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: {
        id: 'owner-1',
        email: 'owner@test.com',
        phoneNumber: null,
        firstName: 'Owner',
        lastName: 'One',
      },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
  };

  async function setup() {
    TestBed.configureTestingModule({
      imports: [InventoryBatchPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: InventoryService, useValue: inventoryService },
        { provide: InventoryDraftIndexedDbService, useValue: draftStorage },
        { provide: ProductCatalogSyncService, useValue: productCatalogSync },
        { provide: SuppliersFacade, useValue: suppliersFacade },
        { provide: AudioService, useValue: audioService },
        { provide: BarcodeDetectorService, useValue: barcodeDetectorService },
        { provide: CameraStreamService, useValue: cameraStreamService },
      ],
    });

    const fixture = TestBed.createComponent(InventoryBatchPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    inventoryService.getProductDetailsByNameOrBarcode.mockReset();
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(of(productDetails));
    inventoryService.addInventoryBatch.mockReset();
    inventoryService.lookupHsn.mockReset();
    inventoryService.lookupHsn.mockReturnValue(of(emptyLookupResult));
    draftStorage.loadRows.mockClear();
    draftStorage.saveRows.mockClear();
    draftStorage.clearRows.mockClear();
    suppliersFacade.load.mockClear();
    productCatalogSync.filterByName.mockClear();
    productCatalogSync.filterByBarcode.mockClear();
    productCatalogSync.findByName.mockClear();
    productCatalogSync.findByBarcode.mockClear();
    audioService.prime.mockClear();
    audioService.beep.mockClear();
    barcodeDetectorService.start.mockClear();
    cameraStreamService.startPreferredCamera.mockClear();
    cameraStreamService.attachToVideo.mockClear();
    cameraStreamService.detachVideo.mockClear();
    cameraStreamService.stopCurrentStream.mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('fetches product details when barcode field loses focus', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    fixture.detectChanges();

    const barcodeAutocomplete = fixture.debugElement.queryAll(By.css('p-autocomplete'))[1];
    barcodeAutocomplete.triggerEventHandler('focusout', new FocusEvent('focusout'));
    await fixture.whenStable();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith('Milk', 'B001');
    expect(component.form.controls.itemDescription.value).toBe(productDetails.description);
    expect(component.form.controls.uom.value).toBe(productDetails.uom);
    expect(component.form.controls.totalPurchaseCost.value).toBe(productDetails.costPrice);
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
    fixture.detectChanges();

    const barcodeAutocomplete = fixture.debugElement.queryAll(By.css('p-autocomplete'))[1];
    barcodeAutocomplete.triggerEventHandler('focusout', new FocusEvent('focusout'));
    await fixture.whenStable();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(undefined, 'B003');
    expect(component.form.controls.itemName.value).toBe(productDetails.name);
  });

  it('keeps selection-triggered lookup and syncs item name from barcode', async () => {
    productCatalogSync.findByBarcode.mockReturnValue({
      name: 'Tea',
      barcode: 'B002',
    });

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.barcode.setValue('B002');
    component.onBarcodeSelected('B002');
    await fixture.whenStable();

    expect(component.form.controls.itemName.value).toBe('Tea');
    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith('Tea', 'B002');
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

    await component['fetchProductDetails']();

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

    await component['fetchProductDetails']();

    expect(component.form.controls.itemDescription.value).toBe(productDetails.description);
    expect(component.form.controls.uom.value).toBe(productDetails.uom);
    expect(component.form.controls.totalPurchaseCost.value).toBe(productDetails.costPrice);
    expect(component.form.controls.mrp.value).toBe(productDetails.mrp);
    expect(component.form.controls.salesPrice.value).toBe(productDetails.salesPrice);
    expect(component.form.controls.supplierName.value).toBe(productDetails.supplierName);
    expect(component.form.controls.taxIncluded.value).toBe(productDetails.taxIncluded);
    expect(component.form.controls.taxRatePercent.value).toBe(productDetails.taxRatePercent);
  });

  it('onItemNameBlur_WithName_CallsLookupHsn', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');

    await component.onItemNameBlur();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Milk');
  });

  it('onItemNameBlur_NameLessThan3Chars_DoesNotCallLookupHsn', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('ab');

    await component.onItemNameBlur();

    expect(inventoryService.lookupHsn).not.toHaveBeenCalled();
  });

  it('onItemNameBlur_SingleHsnAndSingleScenario_AutoAppliesWithoutShowingPicker', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({
        hsnCodes: ['0401'],
        taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
      }),
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

  it('onItemNameBlur_SingleHsnAndSingleScenario_ShowsChip', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({
        hsnCodes: ['0401'],
        taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
      }),
    );

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');

    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.selectedHsnCode()).toBe('0401');
    expect(fixture.debugElement.query(By.css('.hsn-chip'))).not.toBeNull();
  });

  it('onItemNameBlur_MultipleResults_ShowsPickerCard', async () => {
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

  it('onItemNameBlur_ApiError_DoesNotShowPicker', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(throwError(() => new Error('lookup failed')));

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');

    await component.onItemNameBlur();
    fixture.detectChanges();

    expect(component.pickerOpen()).toBe(false);
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).toBeNull();
  });

  it('onItemNameBlur_ApiError_TaxFieldRemainsEditable', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(throwError(() => new Error('lookup failed')));

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');

    await component.onItemNameBlur();
    component.form.controls.taxRatePercent.setValue(22);

    expect(component.form.controls.taxRatePercent.value).toBe(22);
    expect(component.form.controls.taxRatePercent.enabled).toBe(true);
  });

  it('applyHsnSelection_ParsesTaxPercentageStringToNumber', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0401', '18%');

    expect(component.form.controls.taxRatePercent.value).toBe(18);
  });

  it('applyHsnSelection_PatchesTaxRatePercentFormControl', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0401', '12%');

    expect(component.form.controls.taxRatePercent.value).toBe(12);
  });

  it('applyHsnSelection_SetsSelectedHsnCode', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0401', '12%');

    expect(component.selectedHsnCode()).toBe('0401');
  });

  it('applyHsnSelection_ClosesPicker', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    component.pickerOpen.set(true);

    component.applyHsnSelection('0401', '12%');

    expect(component.pickerOpen()).toBe(false);
  });

  it('itemNameChange_AfterApply_ClearsChipAndResetsState', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.applyHsnSelection('0401', '18%');
    component.hsnResult.set({
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
    });
    component.pickerOpen.set(true);
    fixture.detectChanges();

    component.form.controls.itemName.setValue('Curd');
    fixture.detectChanges();

    expect(component.selectedHsnCode()).toBeNull();
    expect(component.hsnResult()).toBeNull();
    expect(component.pickerOpen()).toBe(false);
  });

  it('existingItemWithHsnCode_PrePopulatesChip', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');

    await component['fetchProductDetails']();
    fixture.detectChanges();

    expect(component.selectedHsnCode()).toBe('0401');
    expect(fixture.debugElement.query(By.css('.hsn-chip'))).not.toBeNull();
  });

  it('onNameSelected_WithHsnCodeInProductDetails_AutoPopulatesHsnChip', async () => {
    productCatalogSync.findByName.mockReturnValueOnce({ name: 'Milk', barcode: 'B001' });

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.onNameSelected('Milk');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith('Milk', 'B001');
    expect(component.selectedHsnCode()).toBe('0401');
    expect(fixture.debugElement.query(By.css('.hsn-chip'))).not.toBeNull();
  });

  it('onNameSelected_ProductWithoutStoredHsn_FallsBackToHsnLookup', async () => {
    productCatalogSync.findByName.mockReturnValueOnce({ name: 'Milk', barcode: 'B001' });
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValueOnce(
      of({ ...productDetails, hsnCode: null } as any),
    );
    inventoryService.lookupHsn.mockReturnValueOnce(
      of({ hsnCodes: ['0402'], taxScenarios: [{ condition: 'Standard', taxPercentage: '12%' }] }),
    );

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.onNameSelected('Milk');
    await fixture.whenStable();

    expect(component.selectedHsnCode()).toBe('0402');
    expect(component.form.controls.taxRatePercent.value).toBe(12);
  });

  it('onItemNameBlur_WhileProductLoading_SkipsHsnLookup', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component['loadingProduct'].set(true);
    component.form.controls.itemName.setValue('Milk');
    await component.onItemNameBlur();

    expect(inventoryService.lookupHsn).not.toHaveBeenCalled();
  });

  it('onItemNameBlur_HsnAlreadyAppliedBeforeLookupReturns_DoesNotOpenPicker', async () => {
    // Simulates race: focusout fires before onNameSelected, HSN lookup and product-details
    // load run concurrently. By the time lookupHsn resolves, selectedHsnCode is already set.
    let resolveLookup!: (value: any) => void;
    inventoryService.lookupHsn.mockReturnValueOnce(
      new (require('rxjs').Observable)((observer: any) => {
        resolveLookup = (v) => { observer.next(v); observer.complete(); };
      }),
    );

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    const blurPromise = component.onItemNameBlur();

    // Before lookupHsn resolves, product details arrive and set the HSN code
    component.selectedHsnCode.set('0401');

    // Now resolve the lookup with results
    resolveLookup({ hsnCodes: ['0401'], taxScenarios: [{ condition: 'Standard', taxPercentage: '5%' }] });
    await blurPromise;

    expect(component.pickerOpen()).toBe(false);
  });

  it('submitForm_IncludesHsnCodeInPayload', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    inventoryService.addInventoryBatch.mockReturnValueOnce(
      of({
        requestedCount: 1,
        successCount: 1,
        failedCount: 0,
        succeeded: [{ clientRowId: 'row-1', result: createResult('row-1') }],
        failed: [],
      }),
    );

    component.pendingRows.set([
      createDraftRow('row-1', 'Milk', 'B001', { hsnCode: '0401' }),
    ]);

    component.onSaveAll();
    await fixture.whenStable();

    expect(inventoryService.addInventoryBatch).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [expect.objectContaining({ hsnCode: '0401' })],
      }),
    );
  });

  it('submitForm_DerivesPurchaseTaxIncludedFromSalesTaxMode', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    inventoryService.addInventoryBatch.mockReturnValueOnce(
      of({
        requestedCount: 1,
        successCount: 1,
        failedCount: 0,
        succeeded: [{ clientRowId: 'row-1', result: createResult('row-1') }],
        failed: [],
      }),
    );

    component.pendingRows.set([
      createDraftRow('row-1', 'Milk', 'B001', { taxIncluded: true }),
    ]);

    component.onSaveAll();
    await fixture.whenStable();

    expect(inventoryService.addInventoryBatch).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [expect.objectContaining({ purchaseTaxIncluded: true })],
      }),
    );
  });

  it('buildDraftRow_CapturesSelectedHsnCodeIntoRow', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.uom.setValue('ltr');
    component.form.controls.totalPurchaseCost.setValue(42);
    component.form.controls.mrp.setValue(50);
    component.form.controls.salesPrice.setValue(48);
    component.selectedHsnCode.set('0401');

    component.onAddRow();

    expect(component.pendingRows()).toHaveLength(1);
    expect(component.pendingRows()[0].hsnCode).toBe('0401');
  });

  it('buildDraftRow_NoHsnSelected_StoresNullInRow', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.uom.setValue('ltr');
    component.form.controls.totalPurchaseCost.setValue(42);
    component.form.controls.mrp.setValue(50);
    component.form.controls.salesPrice.setValue(48);

    component.onAddRow();

    expect(component.pendingRows()).toHaveLength(1);
    expect(component.pendingRows()[0].hsnCode).toBeNull();
  });

  it('mapRowsToRequest_UsesPerRowHsnCode_NotGlobalSignal', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    inventoryService.addInventoryBatch.mockReturnValueOnce(
      of({
        requestedCount: 2,
        successCount: 2,
        failedCount: 0,
        succeeded: [
          { clientRowId: 'row-1', result: createResult('row-1') },
          { clientRowId: 'row-2', result: createResult('row-2') },
        ],
        failed: [],
      }),
    );

    component.pendingRows.set([
      createDraftRow('row-1', 'Milk', 'B001', { hsnCode: '0401' }),
      createDraftRow('row-2', 'Curd', 'B002', { hsnCode: '0402' }),
    ]);
    // Signal is null (form was reset) — rows must use their own stored hsnCode
    component.selectedHsnCode.set(null);

    component.onSaveAll();
    await fixture.whenStable();

    const sentItems = inventoryService.addInventoryBatch.mock.calls[0][0].items;
    expect(sentItems[0].hsnCode).toBe('0401');
    expect(sentItems[1].hsnCode).toBe('0402');
  });

  it('onEditRow_RestoresSelectedHsnCodeSignalFromRow', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.pendingRows.set([
      createDraftRow('row-edit-1', 'Milk', 'B010', { hsnCode: '0401' }),
    ]);

    component.onEditRow('row-edit-1');

    expect(component.selectedHsnCode()).toBe('0401');
  });

  it('onEditRow_RowWithNoHsnCode_ClearsSelectedHsnCodeSignal', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.selectedHsnCode.set('0401');
    component.pendingRows.set([
      createDraftRow('row-edit-2', 'Milk', 'B010', { hsnCode: null }),
    ]);

    component.onEditRow('row-edit-2');

    expect(component.selectedHsnCode()).toBeNull();
  });

  it('dismissPicker_KeepsTaxFieldEditable', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.pickerOpen.set(true);
    component.form.controls.taxRatePercent.setValue(9);

    component.dismissPicker();
    component.form.controls.taxRatePercent.setValue(11);

    expect(component.pickerOpen()).toBe(false);
    expect(component.form.controls.taxRatePercent.value).toBe(11);
  });

  it('calls product lookup with barcode only and patches item name from API', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('');
    component.form.controls.barcode.setValue('B009');

    await component['fetchProductDetails']();

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(undefined, 'B009');
    expect(component.form.controls.itemName.value).toBe(productDetails.name);
  });

  it('does not patch supplierName or taxIncluded when batch has no active supplier', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      of({ ...productDetails, supplierId: null, supplierName: null, taxIncluded: null, taxRatePercent: null }) as any,
    );

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');

    await component['fetchProductDetails']();

    expect(component.form.controls.supplierName.value).toBe('');
    expect(component.form.controls.taxIncluded.value).toBe(true);
    expect(component.form.controls.taxRatePercent.value).toBe(0);
  });

  it('does not overwrite supplierName or taxIncluded already edited by user', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.form.controls.itemName.setValue('Milk');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.supplierName.setValue('My Supplier');
    component.form.controls.taxIncluded.setValue(false);

    component.form.controls.supplierName.markAsDirty();
    component.form.controls.taxIncluded.markAsDirty();

    await component['fetchProductDetails']();

    expect(component.form.controls.supplierName.value).toBe('My Supplier');
    expect(component.form.controls.taxIncluded.value).toBe(false);
  });

  it('opens scanner from embedded barcode camera button', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    const button = fixture.debugElement.query(By.css('.camera-addon button'));
    button.triggerEventHandler('click');
    fixture.detectChanges();

    expect(component.isScannerOpen()).toBe(true);
    expect(audioService.prime).toHaveBeenCalledTimes(1);
  });

  it('increments quantity when scanned barcode already exists in pending rows', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const messageService = fixture.debugElement.injector.get(MessageService);
    const addSpy = vi.spyOn(messageService, 'add');
    const barcode = createQrLikeBarcode();

    component.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode,
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 2,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      },
    ]);

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'native',
    });

    expect(component.pendingRows()[0].quantity).toBe(3);
    expect(component.highlightedRowId()).toBe('row-1');
    expect(draftStorage.saveRows).toHaveBeenCalledTimes(1);
    expect(audioService.beep).toHaveBeenCalledTimes(1);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', detail: expect.stringContaining(barcode) }),
    );
  });

  it('adds a new pending row when a scanned barcode resolves to a known product', async () => {
    const barcode = createQrLikeBarcode();
    productCatalogSync.findByBarcode.mockReturnValue({
      name: 'Tea',
      barcode,
    });

    const fixture = await setup();
    const component = fixture.componentInstance;

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'zxing',
    });

    expect(component.pendingRows()).toHaveLength(1);
    expect(component.pendingRows()[0].barcode).toBe(barcode);
    expect(component.pendingRows()[0].itemName).toBe('Tea');
    expect(component.scannerSessionCount()).toBe(1);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionAdded');
    expect(audioService.beep).toHaveBeenCalledTimes(1);
  });

  it('adds a new pending row on scanner cache miss when API returns product details', async () => {
    const barcode = createQrLikeBarcode();
    productCatalogSync.findByBarcode.mockReturnValue(undefined);

    const fixture = await setup();
    const component = fixture.componentInstance;
    const messageService = fixture.debugElement.injector.get(MessageService);
    const addSpy = vi.spyOn(messageService, 'add');
    component.isScannerOpen.set(true);

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'zxing',
    });

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(undefined, barcode);
    expect(component.pendingRows()).toHaveLength(1);
    expect(component.pendingRows()[0].itemName).toBe(productDetails.name);
    expect(component.pendingRows()[0].barcode).toBe(barcode);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionAdded');
    expect(component.isScannerOpen()).toBe(true);
    expect(addSpy).toHaveBeenCalledTimes(1);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', detail: expect.stringContaining(barcode) }),
    );
  });

  it('stops scanner and waits for manual input when only name and barcode are available', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const barcode = createQrLikeBarcode();

    productCatalogSync.findByBarcode.mockReturnValue(undefined);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      of({ ...productDetails, uom: 'Unit', costPrice: 0, mrp: 0, salesPrice: 0 }) as any,
    );
    component.isScannerOpen.set(true);

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'zxing',
    });

    expect(component.pendingRows()).toHaveLength(0);
    expect(component.form.controls.itemName.value).toBe(productDetails.name);
    expect(component.form.controls.barcode.value).toBe(barcode);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionReview');
    expect(component.isScannerOpen()).toBe(false);
  });

  it('populates form and removes row when editing a pending row', async () => {
    suppliersFacade.suppliers.set([
      {
        supplierId: 'supplier-1',
        name: 'Acme Foods',
        email: null,
        contactPerson: null,
        mobileNumber: null,
        gstin: null,
        addressLine1: null,
        addressLine2: null,
        city: null,
        state: null,
        country: null,
        postalCode: null,
        notes: null,
        isActive: true,
      },
    ]);

    const fixture = await setup();
    const component = fixture.componentInstance;

    component.pendingRows.set([
      {
        clientRowId: 'row-edit-1',
        itemName: 'Milk',
        barcode: 'B010',
        itemDescription: 'Fresh milk pack',
        uom: 'ltr',
        batchNumber: 'BN-EDIT-1',
        quantity: 3,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        hsnCode: null,
        expiryDate: '2026-12-31',
        manufacturingDate: '2026-01-01',
        supplierId: 'supplier-1',
        referenceNumber: 'REF-001',
        notes: 'Handle with care',
        performedAt: new Date().toISOString(),
      },
    ]);

    component.onEditRow('row-edit-1');

    expect(component.pendingRows()).toHaveLength(0);
    expect(component.form.controls.itemName.value).toBe('Milk');
    expect(component.form.controls.barcode.value).toBe('B010');
    expect(component.form.controls.itemDescription.value).toBe('Fresh milk pack');
    expect(component.form.controls.batchNumber.value).toBe('BN-EDIT-1');
    expect(component.form.controls.quantity.value).toBe(3);
    expect(component.form.controls.supplierName.value).toBe('Acme Foods');
    expect(draftStorage.clearRows).toHaveBeenCalled();
  });

  it('closes scanner when scanned product cannot be added as a row', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const messageService = fixture.debugElement.injector.get(MessageService);
    const addSpy = vi.spyOn(messageService, 'add');
    const barcode = createQrLikeBarcode();

    productCatalogSync.findByBarcode.mockReturnValue(undefined);
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
      throwError(() => new Error('lookup failed')),
    );
    component.isScannerOpen.set(true);

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'zxing',
    });

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(
      undefined,
      barcode,
    );
    expect(component.pendingRows()).toHaveLength(0);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionReview');
    expect(component.isScannerOpen()).toBe(false);
    expect(addSpy).toHaveBeenCalledTimes(1);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'warn' }),
    );
  });

  it('keeps scanner in review flow when cache misses and API returns no usable product', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const messageService = fixture.debugElement.injector.get(MessageService);
    const addSpy = vi.spyOn(messageService, 'add');
    const barcode = createQrLikeBarcode();

    productCatalogSync.findByBarcode.mockReturnValue(undefined);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(of({ ...productDetails, name: '' }) as any);
    component.isScannerOpen.set(true);

    await component.handleScannedBarcode({
      value: barcode,
      format: 'QR-CODE',
      engine: 'zxing',
    });

    expect(inventoryService.getProductDetailsByNameOrBarcode).toHaveBeenCalledWith(
      undefined,
      barcode,
    );
    expect(component.pendingRows()).toHaveLength(0);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionReview');
    expect(component.isScannerOpen()).toBe(false);
    expect(addSpy).toHaveBeenCalledTimes(1);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'warn' }),
    );
  });

  it('splits save requests into chunks of 100 when more than 100 rows exist', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    const rows = Array.from({ length: 205 }, (_, index) =>
      createDraftRow(`row-${index + 1}`, `Item ${index + 1}`, `BC-${index + 1}`),
    );

    inventoryService.addInventoryBatch.mockImplementation(
      (payload: { items: Array<{ clientRowId: string }> }) =>
        of({
          requestedCount: payload.items.length,
          successCount: payload.items.length,
          failedCount: 0,
          succeeded: payload.items.map((item) => ({
            clientRowId: item.clientRowId,
            result: createResult(item.clientRowId),
          })),
          failed: [],
        }),
    );

    component.pendingRows.set(rows);
    component.onSaveAll();
    await fixture.whenStable();

    expect(inventoryService.addInventoryBatch).toHaveBeenCalledTimes(3);
    expect(inventoryService.addInventoryBatch.mock.calls[0][0].items).toHaveLength(100);
    expect(inventoryService.addInventoryBatch.mock.calls[1][0].items).toHaveLength(100);
    expect(inventoryService.addInventoryBatch.mock.calls[2][0].items).toHaveLength(5);
    expect(component.pendingRows()).toHaveLength(0);
    expect(component.saveSummary()?.requestedCount).toBe(205);
    expect(component.saveSummary()?.successCount).toBe(205);
    expect(component.saveSummary()?.failedCount).toBe(0);
    expect(draftStorage.clearRows).toHaveBeenCalled();
  });

  it('retains failed rows across chunks and persists only failed rows', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    const rows = Array.from({ length: 205 }, (_, index) =>
      createDraftRow(`row-${index + 1}`, `Item ${index + 1}`, `BC-${index + 1}`),
    );

    inventoryService.addInventoryBatch
      .mockReturnValueOnce(
        of({
          requestedCount: 100,
          successCount: 99,
          failedCount: 1,
          succeeded: rows.slice(1, 100).map((row) => ({
            clientRowId: row.clientRowId,
            result: createResult(row.clientRowId),
          })),
          failed: [
            {
              clientRowId: rows[0].clientRowId,
              itemName: rows[0].itemName,
              barcode: rows[0].barcode,
              errors: [{ code: 'Inventory.SomeRule', description: 'Row failed in chunk 1' }],
            },
          ],
        }),
      )
      .mockReturnValueOnce(
        of({
          requestedCount: 100,
          successCount: 99,
          failedCount: 1,
          succeeded: rows.slice(101, 200).map((row) => ({
            clientRowId: row.clientRowId,
            result: createResult(row.clientRowId),
          })),
          failed: [
            {
              clientRowId: rows[100].clientRowId,
              itemName: rows[100].itemName,
              barcode: rows[100].barcode,
              errors: [{ code: 'Inventory.SomeRule', description: 'Row failed in chunk 2' }],
            },
          ],
        }),
      )
      .mockReturnValueOnce(
        of({
          requestedCount: 5,
          successCount: 5,
          failedCount: 0,
          succeeded: rows.slice(200).map((row) => ({
            clientRowId: row.clientRowId,
            result: createResult(row.clientRowId),
          })),
          failed: [],
        }),
      );

    component.pendingRows.set(rows);
    component.onSaveAll();
    await fixture.whenStable();

    expect(component.pendingRows().map((row) => row.clientRowId)).toEqual([
      rows[0].clientRowId,
      rows[100].clientRowId,
    ]);
    expect(component.saveSummary()?.requestedCount).toBe(205);
    expect(component.saveSummary()?.successCount).toBe(203);
    expect(component.saveSummary()?.failedCount).toBe(2);
    expect(draftStorage.saveRows).toHaveBeenCalled();
    expect(draftStorage.clearRows).not.toHaveBeenCalled();
  });

  it('keeps unsent rows when a later chunk request fails', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    const rows = Array.from({ length: 205 }, (_, index) =>
      createDraftRow(`row-${index + 1}`, `Item ${index + 1}`, `BC-${index + 1}`),
    );

    inventoryService.addInventoryBatch
      .mockReturnValueOnce(
        of({
          requestedCount: 100,
          successCount: 99,
          failedCount: 1,
          succeeded: rows.slice(1, 100).map((row) => ({
            clientRowId: row.clientRowId,
            result: createResult(row.clientRowId),
          })),
          failed: [
            {
              clientRowId: rows[0].clientRowId,
              itemName: rows[0].itemName,
              barcode: rows[0].barcode,
              errors: [{ code: 'Inventory.SomeRule', description: 'Row failed in chunk 1' }],
            },
          ],
        }),
      )
      .mockReturnValueOnce(
        throwError(() => ({
          error: {
            errors: [{ code: 'Inventory.ServerError', description: 'Chunk failed unexpectedly' }],
          },
        })),
      );

    component.pendingRows.set(rows);
    component.onSaveAll();
    await fixture.whenStable();

    expect(component.pendingRows()).toHaveLength(106);
    expect(component.pendingRows()[0].clientRowId).toBe(rows[0].clientRowId);
    expect(component.saveSummary()?.successCount).toBe(99);
    expect(component.saveSummary()?.failedCount).toBe(1);
  });

  function createDraftRow(
    clientRowId: string,
    itemName: string,
    barcode: string,
    overrides: Partial<{ hsnCode: string | null; supplierId: string | null; taxIncluded: boolean }> = {},
  ) {
    return {
      clientRowId,
      itemName,
      barcode,
      itemDescription: null,
      uom: 'unit',
      batchNumber: 'BN-20260101-ABCDE',
      quantity: 1,
      totalPurchaseCost: 10,
      mrp: 12,
      salesPrice: 11,
      taxRatePercent: 5,
      taxIncluded: overrides.taxIncluded ?? true,
      purchaseTaxIncluded: overrides.taxIncluded ?? true,
      hsnCode: overrides.hsnCode ?? null,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: overrides.supplierId ?? null,
      referenceNumber: null,
      notes: null,
      performedAt: new Date().toISOString(),
    };
  }

  function createResult(suffix: string) {
    return {
      itemId: `item-${suffix}`,
      itemName: `Item ${suffix}`,
      barcode: `BC-${suffix}`,
      batchId: `batch-${suffix}`,
      batchNumber: 'BN-20260101-ABCDE',
      batchQuantity: 1,
      totalQuantity: 1,
      supplierId: null,
      stockTransactionId: `tx-${suffix}`,
      performedAt: new Date().toISOString(),
    };
  }

  function createQrLikeBarcode() {
    return `QR|01|${crypto.randomUUID()}|TRACE|${crypto.randomUUID()}|PAYLOAD|AAAAAAAAAAAAAAAAAAAAAAAA`;
  }
});
