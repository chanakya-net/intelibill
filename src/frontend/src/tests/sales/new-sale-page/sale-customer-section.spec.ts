import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

  it('shows phone format validation hint when customer phone is invalid', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;
    const element = fixture.nativeElement as HTMLElement;

    vm.customerForm.controls.customerPhone.setValue('invalid-phone');
    vm.customerForm.controls.customerPhone.markAsTouched();
    fixture.detectChanges();

    expect(vm.customerForm.controls.customerPhone.invalid).toBe(true);
    expect(element.textContent).toContain('sales.newSale.invalidPhone');
  });

  it('shows offline customer note only when in offline mode', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    let element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('sales.newSale.customerName');
    expect(element.textContent).not.toContain('sales.newSale.offline.cachedCustomerOnlyNote');

    fixture.destroy();

    TestBed.resetTestingModule();
    setupNewSalePageTestBed({
      networkStatus: {
        isOnline: signal(false),
        canReachApi: signal(false),
        lastVerifiedAt: signal<Date | null>(null),
        isChecking: signal(false),
        checkConnectivity: vi.fn(async () => undefined),
      },
    });
    const offlineFixture = TestBed.createComponent(NewSalePageComponent);
    offlineFixture.detectChanges();
    element = offlineFixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('sales.newSale.offline.cachedCustomerOnlyNote');
  });
});
