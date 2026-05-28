import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: payment section', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('updates selected payment method control', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.onPaymentMethodChanged('Card');
    expect(vm.paymentForm.controls.paymentMethod.value).toBe(3);
  });

  it('submit event fires via onPaymentSubmitRequested', async () => {
    const { deps } = setupNewSalePageTestBed();
    void deps;
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    const submitSpy = vi.spyOn(vm, 'onPaymentSubmitRequested');
    await vm.onPaymentSubmitRequested();
    expect(submitSpy).toHaveBeenCalledOnce();
  });

  it('isSubmitting starts false', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isSubmitting()).toBe(false);
  });
});
