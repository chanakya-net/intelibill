import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { EditSupplierOverlayComponent } from './edit-supplier-overlay.component';
import { SuppliersFacade } from '../state/suppliers.facade';

describe('EditSupplierOverlayComponent', () => {
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');

  const suppliersFacade = {
    isSubmitting: isSubmittingSignal,
    errorMessage: errorSignal,
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    editSupplier: vi.fn(),
  };

  function setup(): EditSupplierOverlayComponent {
    TestBed.configureTestingModule({
      imports: [
        EditSupplierOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: SuppliersFacade, useValue: suppliersFacade }],
    });

    const fixture = TestBed.createComponent(EditSupplierOverlayComponent);
    fixture.componentInstance.supplier = {
      supplierId: 's1',
      name: 'Fresh Foods',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999999',
      address: 'Address',
      city: 'City',
      state: 'State',
      pin: '560001',
      isSystem: false,
      isActive: true,
      isPreferred: false,
      balanceDue: 1500,
    };
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    suppliersFacade.clearError.mockReset();
    suppliersFacade.clearMutationStatus.mockReset();
    suppliersFacade.editSupplier.mockReset();
    isSubmittingSignal.set(false);
    errorSignal.set('');
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when contact phone is invalid', () => {
    const component = setup();

    component.form.controls.contactPersonPhone.setValue('123');
    component.onSubmit();

    expect(component.form.controls.contactPersonPhone.invalid).toBe(true);
    expect(suppliersFacade.editSupplier).not.toHaveBeenCalled();
  });

  it('rejects whitespace-only required supplier fields', () => {
    const component = setup();
    component.form.patchValue({
      name: '   ',
      address: '   ',
      city: '   ',
      state: '   ',
      pin: '   ',
    });

    expect(component.form.invalid).toBe(true);
    expect(component.form.controls.name.hasError('required')).toBe(true);
  });

  it('submits an empty optional contact phone as null', () => {
    const component = setup();

    component.form.controls.contactPersonPhone.setValue('');
    component.onSubmit();

    expect(component.form.controls.contactPersonPhone.valid).toBe(true);
    expect(suppliersFacade.editSupplier).toHaveBeenCalledWith(
      's1',
      expect.objectContaining({ contactPersonPhone: null }),
    );
  });

  it('dispatches edit supplier request with trimmed payload', () => {
    const component = setup();

    component.form.controls.name.setValue('  Fresh Foods Updated  ');
    component.form.controls.contactPersonName.setValue('  Ramesh  ');
    component.form.controls.contactPersonPhone.setValue('+919999999998');
    component.form.controls.address.setValue(' 42 MG Road ');
    component.form.controls.city.setValue(' Bengaluru ');
    component.form.controls.state.setValue(' Karnataka ');
    component.form.controls.pin.setValue(' 560001 ');
    component.form.controls.isActive.setValue(false);
    component.form.controls.isPreferred.setValue(true);

    component.onSubmit();

    expect(suppliersFacade.clearError).toHaveBeenCalled();
    expect(suppliersFacade.clearMutationStatus).toHaveBeenCalled();
    expect(suppliersFacade.editSupplier).toHaveBeenCalledWith('s1', {
      name: 'Fresh Foods Updated',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999998',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pin: '560001',
      isActive: false,
      isPreferred: true,
    });
  });
});
