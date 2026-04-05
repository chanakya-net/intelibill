import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
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
  });
});