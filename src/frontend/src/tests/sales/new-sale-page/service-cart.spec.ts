import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: service lines', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('searches services, adds service line, edits qty/price, and hides discount editor', async () => {
    const deps = setupNewSalePageTestBed().deps;
    deps.saleService.getSellables.mockReturnValueOnce(
      of([
        {
          kind: 'Service',
          serviceId: 'svc-1',
          code: 'S-001',
          name: 'Bike wash',
          description: null,
          price: 100,
          hsnCode: null,
          taxRatePercent: 0,
          taxIncluded: false,
        },
      ])
    );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.searchInput.set('wash');
    await vm.onBarcodeSearch();

    expect(vm.cart()).toHaveLength(0);
    expect(vm.serviceCart()).toHaveLength(1);
    expect(vm.hasServiceLines()).toBe(true);
    expect(vm.isMixedCart()).toBe(false);

    const serviceLineKey = vm.serviceCart()[0].clientLineKey;
    vm.onCartTableQuantityChanged({ itemId: serviceLineKey, qty: 3 });
    expect(vm.serviceCart()[0].quantity).toBe(3);

    vm.onCartTableServiceUnitPriceChanged({ itemId: serviceLineKey, value: 125.5 });
    expect(vm.serviceCart()[0].unitPrice).toBe(125.5);

    vm.onCartTableLineDiscountEditorToggled(serviceLineKey);
    expect(vm.openLineDiscountEditorByKey()[serviceLineKey]).toBeUndefined();

    vm.onPaymentSubmitRequested();
    expect(deps.salesFacade.recordSale).toHaveBeenCalledTimes(0);
  });

  it('service-only submit is blocked without preview payload', async () => {
    const deps = setupNewSalePageTestBed().deps;
    deps.saleService.getSellables.mockReturnValueOnce(
      of([
        {
          kind: 'Service',
          serviceId: 'svc-1',
          code: 'S-001',
          name: 'Bike wash',
          description: null,
          price: 100,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: false,
        },
      ])
    );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.searchInput.set('wash');
    await vm.onBarcodeSearch();
    vm.onPaymentPaidAmountChanged(0);
    vm.onPaymentDueAmountChanged(0);
    await vi.advanceTimersByTimeAsync(300);
    vm.onPaymentSubmitRequested();

    expect(deps.salesFacade.recordSale).toHaveBeenCalledTimes(0);
    expect(vm.paymentSplitError()).toBe('sales.newSale.invalidPaymentSplit');
  });

  it('builds mixed cart state with goods and service lines', async () => {
    const deps = setupNewSalePageTestBed().deps;
    deps.saleService.getSellables.mockReturnValueOnce(
      of([
        {
          kind: 'Service',
          serviceId: 'svc-1',
          code: 'S-001',
          name: 'Bike wash',
          description: null,
          price: 100,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: false,
        },
      ])
    );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.searchInput.set('oreo');
    await vm.onBarcodeSearch();
    vm.searchInput.set('wash');
    await vm.onBarcodeSearch();
    expect(vm.hasGoodsLines()).toBe(true);
    expect(vm.hasServiceLines()).toBe(true);
    expect(vm.isMixedCart()).toBe(true);
    expect(vm.cart()).toHaveLength(1);
    expect(vm.serviceCart()).toHaveLength(1);
  });
});
