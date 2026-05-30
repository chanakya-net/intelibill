import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart table', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('toggles per-line discount editor state', () => {
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

    vm.onCartTableLineDiscountEditorToggled('clk-1');
    expect(vm.openLineDiscountEditorByKey()['clk-1']).toBe(true);

    vm.onCartTableLineDiscountEditorToggled('clk-1');
    expect(vm.openLineDiscountEditorByKey()['clk-1']).toBe(false);
  });
});
