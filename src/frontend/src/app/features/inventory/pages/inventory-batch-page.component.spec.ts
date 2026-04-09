import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
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
  const productDetails = {
    description: 'Fresh milk pack',
    uom: 'ltr',
    costPrice: 42,
    mrp: 50,
    salesPrice: 48,
    supplierId: 'supplier-1',
    supplierName: 'Acme Foods',
    taxIncluded: true as boolean | null,
    taxRatePercent: 18 as number | null,
  };

  const inventoryService = {
    getProductDetailsByNameOrBarcode: vi.fn(() => of(productDetails)),
    addInventoryBatch: vi.fn(),
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
    suppliers: signal([]),
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
    expect(component.form.controls.costPrice.value).toBe(productDetails.costPrice);
    expect(component.form.controls.mrp.value).toBe(productDetails.mrp);
    expect(component.form.controls.salesPrice.value).toBe(productDetails.salesPrice);
    expect(component.form.controls.supplierName.value).toBe(productDetails.supplierName);
    expect(component.form.controls.taxIncluded.value).toBe(productDetails.taxIncluded);
    expect(component.form.controls.taxRatePercent.value).toBe(productDetails.taxRatePercent);
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
    component.form.controls.costPrice.setValue(10);
    component.form.controls.mrp.setValue(12);
    component.form.controls.salesPrice.setValue(11);

    component.form.controls.itemDescription.markAsDirty();
    component.form.controls.uom.markAsDirty();
    component.form.controls.costPrice.markAsDirty();
    component.form.controls.mrp.markAsDirty();
    component.form.controls.salesPrice.markAsDirty();

    await component['fetchProductDetails']();

    expect(component.form.controls.itemDescription.value).toBe('Manual description');
    expect(component.form.controls.uom.value).toBe('box');
    expect(component.form.controls.costPrice.value).toBe(10);
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
    expect(component.form.controls.costPrice.value).toBe(productDetails.costPrice);
    expect(component.form.controls.mrp.value).toBe(productDetails.mrp);
    expect(component.form.controls.salesPrice.value).toBe(productDetails.salesPrice);
    expect(component.form.controls.supplierName.value).toBe(productDetails.supplierName);
    expect(component.form.controls.taxIncluded.value).toBe(productDetails.taxIncluded);
    expect(component.form.controls.taxRatePercent.value).toBe(productDetails.taxRatePercent);
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
    expect(component.form.controls.taxIncluded.value).toBe(false);
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

    const button = fixture.debugElement.query(By.css('.barcode-camera-addon button'));
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

    component.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 2,
        costPrice: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      },
    ]);

    await component.handleScannedBarcode({
      value: 'B001',
      format: 'CODE-128',
      engine: 'native',
    });

    expect(component.pendingRows()[0].quantity).toBe(3);
    expect(component.highlightedRowId()).toBe('row-1');
    expect(draftStorage.saveRows).toHaveBeenCalledTimes(1);
    expect(audioService.beep).toHaveBeenCalledTimes(1);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', detail: expect.stringContaining('B001') }),
    );
  });

  it('adds a new pending row when a scanned barcode resolves to a known product', async () => {
    productCatalogSync.findByBarcode.mockReturnValue({
      name: 'Tea',
      barcode: 'B002',
    });

    const fixture = await setup();
    const component = fixture.componentInstance;

    await component.handleScannedBarcode({
      value: 'B002',
      format: 'CODE-128',
      engine: 'zxing',
    });

    expect(component.pendingRows()).toHaveLength(1);
    expect(component.pendingRows()[0].barcode).toBe('B002');
    expect(component.pendingRows()[0].itemName).toBe('Tea');
    expect(component.scannerSessionCount()).toBe(1);
    expect(component.scannerLastAction()).toBe('inventory.scannerActionAdded');
    expect(audioService.beep).toHaveBeenCalledTimes(1);
  });
});