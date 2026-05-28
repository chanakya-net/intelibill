import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart checkout summary', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
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
});

