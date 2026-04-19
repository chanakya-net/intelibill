import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

    TestBed.configureTestingModule({
      imports: [NewSalePageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: InventoryService, useValue: inventoryService },
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
});
