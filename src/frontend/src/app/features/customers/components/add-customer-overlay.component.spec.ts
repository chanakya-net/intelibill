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
    });
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
