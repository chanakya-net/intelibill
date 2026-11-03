import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { AddCustomerOverlayComponent } from './add-customer-overlay.component';
import { CustomersFacade } from '../state/customers.facade';

describe('AddCustomerOverlayComponent', () => {
  function setup() {
    const facade = {
      submitting: signal(false),
      errorMessage: signal(''),
      clearError: vi.fn(),
      clearMutationStatus: vi.fn(),
      addCustomer: vi.fn(),
    } as unknown as CustomersFacade;

    TestBed.configureTestingModule({
      imports: [
        AddCustomerOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: CustomersFacade, useValue: facade }],
    });

    const fixture = TestBed.createComponent(AddCustomerOverlayComponent);
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance, facade };
  }

  it('defaults creditLimit to 0', () => {
    const { component } = setup();
    expect(component.form.controls.creditLimit.value).toBe(0);
  });

  it('submits payload with numeric creditLimit', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: ' Alice ',
      phoneNumber: '+919812345678',
      address: '',
      isActive: true,
      creditLimit: 0,
    });

    component.onSubmit();

    expect(facade.addCustomer).toHaveBeenCalledWith({
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: null,
      isActive: true,
      creditLimit: 0,
      gstin: null,
      stateCode: null,
    });
  });

  it('submits the GSTIN in capitals with its state', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Bharat Stores',
      phoneNumber: '+919811111111',
      address: '5 Market Lane',
      gstin: ' 29aagcb7383j1z4 ',
      stateCode: '29',
    });

    component.onSubmit();

    expect(facade.addCustomer).toHaveBeenCalledWith(
      expect.objectContaining({ gstin: '29AAGCB7383J1Z4', stateCode: '29' }),
    );
  });

  it('accepts a state without a GSTIN', () => {
    const { component, facade } = setup();
    component.form.patchValue({ name: 'Walk-in Regular', phoneNumber: '+919811111111', stateCode: '27' });

    component.onSubmit();

    expect(facade.addCustomer).toHaveBeenCalledWith(expect.objectContaining({ gstin: null, stateCode: '27' }));
  });

  it('rejects a GSTIN with a wrong check digit', () => {
    const { component, facade } = setup();
    component.form.patchValue({ name: 'Bharat Stores', phoneNumber: '+919811111111', gstin: '29AAGCB7383J1Z5' });

    component.onSubmit();

    expect(component.form.controls.gstin.hasError('gstinChecksum')).toBe(true);
    expect(facade.addCustomer).not.toHaveBeenCalled();
  });

  it('rejects a GSTIN from a different state than the one chosen', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Bharat Stores',
      phoneNumber: '+919811111111',
      gstin: '29AAGCB7383J1Z4',
      stateCode: '27',
    });

    component.onSubmit();

    expect(component.form.hasError('gstinStateMismatch')).toBe(true);
    expect(facade.addCustomer).not.toHaveBeenCalled();
  });

  it('rejects negative creditLimit', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Alice',
      phoneNumber: '+919812345678',
      address: '',
      isActive: true,
      creditLimit: -1,
    });

    component.onSubmit();

    expect(component.form.invalid).toBe(true);
    expect(facade.addCustomer).not.toHaveBeenCalled();
  });

  it('rejects credit limits with more than two decimal places', () => {
    const { component } = setup();
    component.form.controls.creditLimit.setValue(1.234);

    expect(component.form.controls.creditLimit.hasError('maxFractionDigits')).toBe(true);
  });

  it('rejects whitespace-only required customer fields', () => {
    const { component } = setup();
    component.form.patchValue({ name: '   ', phoneNumber: '   ' });

    expect(component.form.controls.name.hasError('required')).toBe(true);
    expect(component.form.controls.phoneNumber.hasError('required')).toBe(true);
  });
});
