import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AddSupplierOverlayComponent } from './add-supplier-overlay.component';
import { SuppliersFacade } from '../state/suppliers.facade';

describe('AddSupplierOverlayComponent', () => {
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');

  const suppliersFacade = {
    isSubmitting: isSubmittingSignal,
    errorMessage: errorSignal,
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    addSupplier: vi.fn(),
  };

  function setup(): AddSupplierOverlayComponent {
    TestBed.configureTestingModule({
      imports: [AddSupplierOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: SuppliersFacade, useValue: suppliersFacade }],
    });

    const fixture = TestBed.createComponent(AddSupplierOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    suppliersFacade.clearError.mockReset();
    suppliersFacade.clearMutationStatus.mockReset();
    suppliersFacade.addSupplier.mockReset();
    isSubmittingSignal.set(false);
    errorSignal.set('');
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when contact phone is invalid', () => {
    const component = setup();

    component.form.controls.name.setValue('Supplier');
    component.form.controls.contactPersonPhone.setValue('123');
    component.form.controls.address.setValue('Address');
    component.form.controls.city.setValue('City');
    component.form.controls.state.setValue('State');
    component.form.controls.pin.setValue('560001');

    component.onSubmit();

    expect(component.form.controls.contactPersonPhone.invalid).toBe(true);
    expect(suppliersFacade.addSupplier).not.toHaveBeenCalled();
  });

  it('dispatches add supplier request with trimmed payload', () => {
    const component = setup();

    component.form.controls.name.setValue('  Fresh Foods  ');
    component.form.controls.contactPersonName.setValue('  Ramesh  ');
    component.form.controls.contactPersonPhone.setValue('+919999999999');
    component.form.controls.address.setValue(' 42 MG Road ');
    component.form.controls.city.setValue(' Bengaluru ');
    component.form.controls.state.setValue(' Karnataka ');
    component.form.controls.pin.setValue(' 560001 ');
    component.form.controls.isActive.setValue(true);
    component.form.controls.isPreferred.setValue(true);

    component.onSubmit();

    expect(suppliersFacade.clearError).toHaveBeenCalled();
    expect(suppliersFacade.clearMutationStatus).toHaveBeenCalled();
    expect(suppliersFacade.addSupplier).toHaveBeenCalledWith({
      name: 'Fresh Foods',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999999',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pin: '560001',
      isActive: true,
      isPreferred: true,
    });
  });
});
