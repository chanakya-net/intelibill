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
});
