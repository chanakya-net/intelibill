import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { EditCustomerOverlayComponent } from './edit-customer-overlay.component';
import { Customer } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';

describe('EditCustomerOverlayComponent', () => {
  function setup(customer: Customer) {
    const facade = {
      submitting: signal(false),
      errorMessage: signal(''),
      clearError: vi.fn(),
      clearMutationStatus: vi.fn(),
      editCustomer: vi.fn(),
    } as unknown as CustomersFacade;

    TestBed.configureTestingModule({
      imports: [
        EditCustomerOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: CustomersFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(EditCustomerOverlayComponent);
    fixture.componentRef.setInput('customer', customer);
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance, facade };
  }

  it('patches creditLimit from customer input', () => {
    const customer: Customer = {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 1500,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
    };

    const { component } = setup(customer);
    expect(component.form.controls.creditLimit.value).toBe(1500);
  });

  it('submits payload with updated creditLimit', () => {
    const customer: Customer = {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 1500,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
    };

    const { component, facade } = setup(customer);
    component.form.patchValue({ creditLimit: 2500 });

    component.onSubmit();

    expect(facade.editCustomer).toHaveBeenCalledWith('c1', {
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 2500,
      gstin: '',
      stateCode: '',
    });
  });

  it('patches and resubmits the customer GSTIN and state', () => {
    const customer: Customer = {
      customerId: 'c2',
      name: 'Bharat Stores',
      phoneNumber: '+919811111111',
      address: '5 Market Lane',
      isActive: true,
      creditLimit: 0,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
      gstin: '29AAGCB7383J1Z4',
      stateCode: '29',
    };

    const { component, facade } = setup(customer);
    expect(component.form.controls.gstin.value).toBe('29AAGCB7383J1Z4');
    expect(component.form.controls.stateCode.value).toBe('29');

    component.form.patchValue({ stateCode: '27' });
    component.onSubmit();
    expect(facade.editCustomer).not.toHaveBeenCalled();
    expect(component.form.hasError('gstinStateMismatch')).toBe(true);

    // An emptied GSTIN is sent as '' so the server clears it rather than keeping the recorded one.
    component.form.patchValue({ gstin: '', stateCode: '27' });
    component.onSubmit();
    expect(facade.editCustomer).toHaveBeenCalledWith('c2', expect.objectContaining({ gstin: '', stateCode: '27' }));
  });

  it('rejects overly large creditLimit', () => {
    const customer: Customer = {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 1500,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
    };

    const { component, facade } = setup(customer);
    component.form.patchValue({ creditLimit: 100000000 });

    component.onSubmit();

    expect(component.form.invalid).toBe(true);
    expect(facade.editCustomer).not.toHaveBeenCalled();
  });

  it('rejects whitespace-only required customer fields', () => {
    const customer: Customer = {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 0,
      purchaseCount: 0,
      lifetimeRevenue: 0,
      currentMonthRevenue: 0,
    };
    const { component } = setup(customer);
    component.form.patchValue({ name: '   ', phoneNumber: '   ' });

    expect(component.form.controls.name.hasError('required')).toBe(true);
    expect(component.form.controls.phoneNumber.hasError('required')).toBe(true);
  });
});
