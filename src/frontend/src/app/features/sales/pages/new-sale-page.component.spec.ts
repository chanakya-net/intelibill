import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { SalesFacade } from '../state/sales.facade';
import { NewSalePageComponent } from './new-sale-page.component';

describe('NewSalePageComponent', () => {
  const inventoryService = {
    getAvailableBatchesBySearchTerm: vi.fn(),
  };

  const router = {
    navigate: vi.fn(),
  };

  const salesFacade = {
    submitting: signal(false),
    errorMessage: signal(''),
    lastMutationSucceeded: signal(false),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    recordSale: vi.fn(),
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

  const cartStorage = {
    loadCart: vi.fn(async () => []),
    saveCart: vi.fn(async () => undefined),
    clearCart: vi.fn(async () => undefined),
  };

  const productCatalogSync = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
  };

  beforeEach(() => {
    inventoryService.getAvailableBatchesBySearchTerm.mockReset();
    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(
      of([
        {
          barcode: 'BC-001',
          itemName: 'Oreo',
          batchNumber: 'B-01',
          quantity: 10,
          salesPrice: 50,
          mrp: 60,
          taxRatePercent: 18,
          taxIncluded: true,
          expiryDate: null,
        },
      ])
    );

    router.navigate.mockReset();
    salesFacade.clearError.mockReset();
    salesFacade.clearMutationStatus.mockReset();
    salesFacade.recordSale.mockReset();
    salesFacade.submitting.set(false);
    salesFacade.errorMessage.set('');
    salesFacade.lastMutationSucceeded.set(false);
    cartStorage.loadCart.mockClear();
    cartStorage.saveCart.mockClear();
    cartStorage.clearCart.mockClear();
    productCatalogSync.filterByName.mockClear();
    productCatalogSync.filterByBarcode.mockClear();

    TestBed.configureTestingModule({
      imports: [NewSalePageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: ProductCatalogSyncService, useValue: productCatalogSync },
        { provide: InventoryService, useValue: inventoryService },
        { provide: SalesCartIndexedDbService, useValue: cartStorage },
        { provide: Router, useValue: router },
        { provide: SalesFacade, useValue: salesFacade },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('opens and closes scanner dialog state', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.openScanner();
    expect(component.isScannerOpen()).toBe(true);

    component.onScannerVisibilityChange(false);
    expect(component.isScannerOpen()).toBe(false);
  });

  it('triggers search immediately when scanner detects a barcode', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    component.isScannerOpen.set(true);

    component.onScannedBarcode({ value: 'BC-001', format: 'CODE-128', engine: 'native' });

    expect(component.searchInput()).toBe('');
    expect(component.isScannerOpen()).toBe(false);
    expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith('BC-001');
    expect(component.cart()).toHaveLength(1);
    expect(component.cart()[0].quantity).toBe(1);
    expect(component.showBatchPicker()).toBe(false);
    expect(component.searchInput()).toBe('');
  });

  it('ignores empty scanned values', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.onScannedBarcode({ value: '   ', format: 'CODE-128', engine: 'native' });

    expect(inventoryService.getAvailableBatchesBySearchTerm).not.toHaveBeenCalled();
  });

  it('increments and decrements cart quantity within stock limits', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.searchInput.set('oreo');
    component.onBarcodeSearch();

    expect(component.cart()[0].quantity).toBe(1);

    component.onIncreaseCartItem(0);
    expect(component.cart()[0].quantity).toBe(2);

    for (let i = 0; i < 20; i++) {
      component.onIncreaseCartItem(0);
    }
    expect(component.cart()[0].quantity).toBe(10);

    component.onDecreaseCartItem(0);
    expect(component.cart()[0].quantity).toBe(9);

    for (let i = 0; i < 20; i++) {
      component.onDecreaseCartItem(0);
    }
    expect(component.cart()[0].quantity).toBe(1);
  });

  it('calculates subtotal, tax, and final totals for included and excluded tax prices', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        barcode: 'A',
        itemName: 'Tax Included',
        batchNumber: 'B-INC',
        quantity: 2,
        availableQuantity: 5,
        salesPrice: 118,
        mrp: 118,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
      },
      {
        barcode: 'B',
        itemName: 'Tax Excluded',
        batchNumber: 'B-EXC',
        quantity: 1,
        availableQuantity: 3,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 18,
        taxIncluded: false,
        costPrice: 0,
      },
    ]);

    expect(component.subtotalAmount()).toBeCloseTo(300, 6);
    expect(component.totalTaxAmount()).toBeCloseTo(54, 6);
    expect(component.totalAmount()).toBeCloseTo(354, 6);
  });

  it('clears cart when clear action is used', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        barcode: 'A',
        itemName: 'Tax Included',
        batchNumber: 'B-INC',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 118,
        mrp: 118,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
      },
    ]);

    component.onClearCart();

    expect(component.cart()).toHaveLength(0);
  });
});
