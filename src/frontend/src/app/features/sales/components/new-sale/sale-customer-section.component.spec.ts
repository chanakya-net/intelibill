import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { FormControl } from '@angular/forms';
import { CustomerDto, SaleCustomerSectionComponent } from './sale-customer-section.component';

describe('SaleCustomerSectionComponent', () => {
  const customer: CustomerDto = {
    customerId: 'cust-1',
    name: 'Alice',
    phoneNumber: '+911234567890',
    address: null,
  };

  it('emits full customer on suggestion selection', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const selectionSpy = vi.fn();

    component.customerSelected.subscribe(selectionSpy);
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });
    component.customers = [customer];

    component.onCustomerSelect(customer.name);

    expect(selectionSpy).toHaveBeenCalledWith(customer);
  });

  it('emits search query changes', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const searchSpy = vi.fn();
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });

    component.searchCustomers.subscribe(searchSpy);

    component.onCustomerNameSearch({ query: 'ali' } as never);

    expect(searchSpy).toHaveBeenCalledWith('ali');
  });

  it('emits null when suggestion does not resolve', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const selectionSpy = vi.fn();
    component.customerSelected.subscribe(selectionSpy);
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });
    component.customers = [customer];

    component.onCustomerSelect('Bob');

    expect(selectionSpy).toHaveBeenCalledWith(null);
  });

  it('does not emit stale selected customer when suggestion selected', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const selectedSpy = vi.fn();
    component.selectedCustomer = customer;
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });
    component.customerSelected.subscribe(selectedSpy);
    component.customers = [customer];

    component.onCustomerSelect(customer.name);

    expect(selectedSpy).toHaveBeenCalledWith(customer);
  });

  it('shows invalid phone validation message', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('abc', {
      nonNullable: true,
      validators: [() => ({ pattern: true })],
    });
    component.customerPhoneControl.markAsTouched();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('sales.newSale.invalidPhone');
  });
});
