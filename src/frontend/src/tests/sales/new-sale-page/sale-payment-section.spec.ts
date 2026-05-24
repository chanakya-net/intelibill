import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

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
});
