import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import type { CustomerDto } from '../../../app/features/sales/components/new-sale/sale-customer-section.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: customer section', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('tracks selected customer id and enables credit usage', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    const customer: CustomerDto = { customerId: 'cust-1', name: 'Alice', phoneNumber: '+919999111222', address: null };
    vm.onCustomerSectionSelected(customer);

    expect(vm.selectedCustomerId()).toBe('cust-1');
    expect(vm.canUseCredit()).toBe(true);
  });
});

