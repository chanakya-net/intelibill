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

  it('emits customer suggestion selection', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const selectionSpy = vi.fn();

    component.customerSuggestionSelected.subscribe(selectionSpy);
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });

    component.onCustomerSelect(customer.name);

    expect(selectionSpy).toHaveBeenCalledWith('Alice');
  });

  it('emits suggestion events', () => {
    TestBed.configureTestingModule({
      imports: [SaleCustomerSectionComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleCustomerSectionComponent);
    const component = fixture.componentInstance;
    const searchSpy = vi.fn();
    const selectSpy = vi.fn();
    component.customerNameControl = new FormControl('', { nonNullable: true });
    component.customerPhoneControl = new FormControl('', { nonNullable: true });

    component.searchCustomers.subscribe(searchSpy);
    component.customerSuggestionSelected.subscribe(selectSpy);

    component.onCustomerNameSearch({ query: 'ali' } as never);
    component.onCustomerSelect('Alice');

    expect(searchSpy).toHaveBeenCalledWith('ali');
    expect(selectSpy).toHaveBeenCalledWith('Alice');
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

    component.onCustomerSelect(customer.name);

    expect(selectedSpy).not.toHaveBeenCalled();
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
