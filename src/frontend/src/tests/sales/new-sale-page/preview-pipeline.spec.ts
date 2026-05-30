import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: preview pipeline', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    vi.useFakeTimers();
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('debounces preview requests when cart changes online', async () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(deps.saleService.previewSale).not.toHaveBeenCalled();

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

    await vi.advanceTimersByTimeAsync(299);
    expect(deps.saleService.previewSale).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(2);
    expect(deps.saleService.previewSale).toHaveBeenCalledTimes(1);
  });
});

