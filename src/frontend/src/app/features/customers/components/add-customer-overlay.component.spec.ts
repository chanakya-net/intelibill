import { readFileSync } from 'node:fs';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { AddCustomerOverlayComponent } from './add-customer-overlay.component';
import { AuthService } from '../../../core/auth/auth.service';
import { CustomersFacade } from '../state/customers.facade';

let overlayRole = 'Owner';

describe('AddCustomerOverlayComponent', () => {
  function setup() {
    const facade = {
      submitting: signal(false),
      errorMessage: signal('errors.customers.addFailed'),
      localizedError: signal({
        messageKey: 'errors.customers.addFailed',
        params: {},
        correlationId: 'form-ref:123',
        fieldErrors: [{ field: 'gstin', messageKey: 'apiErrors.invalidField', params: {} }],
      }),
      clearError: vi.fn(),
      clearMutationStatus: vi.fn(),
      addCustomer: vi.fn(),
    } as unknown as CustomersFacade;

    TestBed.configureTestingModule({
      imports: [
        AddCustomerOverlayComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': JSON.parse(readFileSync('public/assets/i18n/en-IN.json', 'utf8')),
            'hi-IN': JSON.parse(readFileSync('public/assets/i18n/hi-IN.json', 'utf8')),
          },
          translocoConfig: {
            availableLangs: ['en-IN', 'hi-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
          preloadLangs: true,
        }),
      ],
      providers: [
        { provide: CustomersFacade, useValue: facade },
        {
          provide: AuthService,
          useValue: {
            session: () => ({
              activeShopId: 'shop-1',
              shops: [{ shopId: 'shop-1', role: overlayRole }],
            }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(AddCustomerOverlayComponent);
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance, facade };
  }

  it('links recognized server fields and displays a support reference', async () => {
    const { fixture } = setup();
    const input = fixture.nativeElement.querySelector(
      'input[formcontrolname="gstin"]',
    ) as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const description = input.getAttribute('aria-describedby');
    expect(description).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#' + description)).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('form-ref:123');
    const english = fixture.nativeElement.textContent;
    TestBed.inject(TranslocoService).setActiveLang('hi-IN');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toBe(english);
    expect(fixture.nativeElement.textContent).toContain('form-ref:123');
  });

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
      creditPolicyMode: 'NoCredit',
      creditTermsDays: null,
      gstin: null,
      stateCode: null,
    });
  });

  it('keeps credit settings read-only for staff and sends no credit', () => {
    overlayRole = 'Staff';
    try {
      const { component, facade } = setup();
      component.form.patchValue({ name: 'Alice', phoneNumber: '+919812345678' });

      component.onSubmit();

      expect(component.canEditCredit()).toBe(false);
      expect(component.form.controls.creditLimit.disabled).toBe(true);
      expect(facade.addCustomer).toHaveBeenCalledWith(
        expect.objectContaining({
          creditLimit: 0,
          creditPolicyMode: 'NoCredit',
          creditTermsDays: null,
        }),
      );
    } finally {
      overlayRole = 'Owner';
    }
  });

  it('refuses an enforced policy without a limit, so zero never reads as unlimited', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Alice',
      phoneNumber: '+919812345678',
      creditLimit: 0,
      creditPolicyMode: 'Enforced',
    });

    component.onSubmit();

    expect(component.form.hasError('enforcedCreditRequiresLimit')).toBe(true);
    expect(facade.addCustomer).not.toHaveBeenCalled();
  });

  it('submits an enforced policy with agreed terms', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Alice',
      phoneNumber: '+919812345678',
      creditLimit: 2000,
      creditPolicyMode: 'Enforced',
      creditTermsDays: 15,
    });

    component.onSubmit();

    expect(facade.addCustomer).toHaveBeenCalledWith(
      expect.objectContaining({
        creditLimit: 2000,
        creditPolicyMode: 'Enforced',
        creditTermsDays: 15,
      }),
    );
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
    component.form.patchValue({
      name: 'Walk-in Regular',
      phoneNumber: '+919811111111',
      stateCode: '27',
    });

    component.onSubmit();

    expect(facade.addCustomer).toHaveBeenCalledWith(
      expect.objectContaining({ gstin: null, stateCode: '27' }),
    );
  });

  it('rejects a GSTIN with a wrong check digit', () => {
    const { component, facade } = setup();
    component.form.patchValue({
      name: 'Bharat Stores',
      phoneNumber: '+919811111111',
      gstin: '29AAGCB7383J1Z5',
    });

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
