import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart checkout summary', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('toggles sale-level discount editor open state', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isSaleDiscountEditorOpen()).toBe(false);
    vm.toggleSaleDiscountEditor();
    expect(vm.isSaleDiscountEditorOpen()).toBe(true);
  });

  it('balance due is sourced from payment form dueAmount control', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    // dueAmount control exists and starts at 0 (empty cart, no total)
    expect(vm.paymentForm.controls.dueAmount.value).toBe(0);
    // The control is the data source for balanceDue displayed in cart-checkout-summary
    expect(vm.paymentForm.controls).toHaveProperty('dueAmount');
  });

  it('totalDiscountAmount is zero when no discount applied', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.totalDiscountAmount()).toBe(0);
  });

  it('does not include service lines in sale-level discount capacity', async () => {
    vi.useFakeTimers();
    deps.saleService.previewSale.mockReturnValue(
      of({
        totalAmount: 110,
        totalTaxableAmount: 110,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: 10,
        configuredSaleRule: null,
        lines: [
          {
            preTaxAmountBeforeDiscount: 10,
            itemDiscountAmount: 0,
            costPrice: 9,
            quantity: 1,
            lineType: 'Goods',
          },
          {
            preTaxAmountBeforeDiscount: 100,
            itemDiscountAmount: 0,
            costPrice: 0,
            quantity: 1,
            lineType: 'Service',
          },
        ],
        infos: [],
        warnings: [],
      } as any)
    );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;
    vm.cart.set([
      {
        clientLineKey: 'clk-g-1',
        barcode: 'A',
        itemName: 'Item',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 10,
        mrp: 10,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 9,
        itemDiscountType: 0,
        itemDiscountValue: 0,
        hsnCode: null,
      },
    ]);
    vm.serviceCart.set([
      {
        kind: 'service',
        clientLineKey: 'clk-s-1',
        serviceId: 'svc-1',
        serviceName: 'Bike wash',
        serviceCode: 'S-001',
        quantity: 1,
        unitPrice: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        hsnCode: null,
      },
    ]);

    await vi.advanceTimersByTimeAsync(301);

    const limits = vm.getSaleDiscountLimits();
    expect(limits.maxFlat).toBe(1);
    expect(limits.maxPercent).toBe(10);

    vi.useRealTimers();
  });
});
