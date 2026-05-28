import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: full flow', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    vi.useFakeTimers();
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('covers lookup, quick add, cart edits, checkout details, and submit request', async () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.searchInput.set('oreo');
    await vm.onBarcodeSearch();

    expect(vm.cart()).toHaveLength(1);
    expect(vm.cart()[0].inventoryBatchId).toBe('batch-1');

    vm.availableBatches.set([
      {
        barcode: 'QR-2',
        itemName: 'Milkshake',
        batchNumber: 'MS-1',
        inventoryBatchId: 'batch-100',
        quantity: 6,
        salesPrice: 75,
        mrp: 80,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
    ]);
    vm.onQuickProductTileSelected(vm.availableBatches()[0]);

    expect(vm.cart()).toHaveLength(2);
    expect(vm.cart()[1].inventoryBatchId).toBe('batch-100');

    const firstLineKey = vm.cart()[0].clientLineKey;
    vm.onCartTableQuantityChanged({ itemId: firstLineKey, qty: 2 });
    vm.onCartTableLineDiscountEditorToggled(firstLineKey);
    vm.onCartTableHsnCodeChange({ itemId: firstLineKey, value: '0902' });
    vm.onCartTableTaxRateChange({ itemId: firstLineKey, value: 12.5 });
    vm.onCartTableDiscountTypeChange({ itemId: firstLineKey, value: 1 });
    vm.onCartTableDiscountValueChange({ itemId: firstLineKey, value: '5.5' });

    expect(vm.openLineDiscountEditorByKey()[firstLineKey]).toBe(true);
    expect(vm.cart()[0].quantity).toBe(2);
    expect(vm.cart()[0].hsnCode).toBe('0902');
    expect(vm.cart()[0].taxRatePercent).toBe(12.5);
    expect(vm.cart()[0].itemDiscountType).toBe(1);
    expect(vm.cart()[0].itemDiscountValue).toBe(5.5);

    vm.onCustomerSectionSelected({
      customerId: 'cust-1',
      name: 'Alice',
      phoneNumber: '+919999111222',
      address: null,
    });
    vm.onPaymentMethodChanged('Credit');
    vm.onPaymentPaidAmountChanged(40);
    vm.onPaymentDueAmountChanged(10);

    await vi.advanceTimersByTimeAsync(300);
    fixture.detectChanges();

    expect(vm.checkoutPreview()).not.toBeNull();
    expect(vm.totalAmount()).toBe(50);
    expect(vm.paymentForm.controls.paymentMethod.value).toBe(4);
    expect(vm.paymentForm.controls.paidAmount.value).toBe(40);
    expect(vm.paymentForm.controls.dueAmount.value).toBe(10);
    expect(vm.canUseCredit()).toBe(true);

    await vm.onPaymentSubmitRequested();

    expect(deps.salesFacade.recordSale).toHaveBeenCalledTimes(1);
    expect(deps.salesFacade.recordSale).toHaveBeenCalledWith(
      expect.objectContaining({
        customerId: 'cust-1',
        customerName: 'Alice',
        customerPhone: '+919999111222',
        paymentMethod: 4,
        paidAmount: 40,
        dueAmount: 10,
        items: expect.arrayContaining([
          expect.objectContaining({
            clientLineKey: firstLineKey,
            itemName: 'Oreo',
          }),
          expect.objectContaining({
            inventoryBatchId: 'batch-100',
            itemName: 'Milkshake',
          }),
        ]),
      })
    );
  });
});
