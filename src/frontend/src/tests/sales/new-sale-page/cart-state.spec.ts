import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart state', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('adds first search result directly to cart', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.searchInput.set('oreo');
    void vm.onBarcodeSearch();

    expect(vm.cart()).toHaveLength(1);
    expect(vm.cart()[0].inventoryBatchId).toBe('batch-1');
  });

  it('clears cart via handler', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'A',
        itemName: 'Item',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
        hsnCode: null,
      },
    ]);

    vm.onClearCart();
    expect(vm.cart()).toHaveLength(0);
  });
});

