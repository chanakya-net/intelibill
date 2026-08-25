import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { InventoryService } from '../../inventory/services/inventory.service';
import { ServiceService } from '../services/service.service';
import { AddServiceOverlayComponent } from './add-service-overlay.component';

describe('AddServiceOverlayComponent', () => {
  const serviceService = {
    addService: vi.fn(() =>
      of({
        serviceId: 'srv-1',
        code: 'SRV-0001',
        name: 'Installation',
        description: null,
        price: 250,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      }),
    ),
  };

  const inventoryService = {
    lookupHsn: vi.fn(() =>
      of({
        hsnCodes: ['9987'],
        taxScenarios: [{ taxPercentage: '18%' }],
      }),
    ),
  };

  beforeEach(() => {
    serviceService.addService.mockClear();
    inventoryService.lookupHsn.mockClear();
    TestBed.configureTestingModule({
      imports: [
        AddServiceOverlayComponent,
        ReactiveFormsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: ServiceService, useValue: serviceService },
        { provide: InventoryService, useValue: inventoryService },
      ],
    });
  });

  it('starts with an empty add form', () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.form.value).toEqual({
      name: '',
      description: '',
      price: 0,
      taxIncluded: true,
      taxRatePercent: 0,
      hsnCode: '',
    });
  });

  it('renders through the shared PrimeNG editor dialog controls', async () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.controls.name.setValue('Installation');
    component.onNameBlur();
    await Promise.resolve();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('p-dialog')).toBeTruthy();
    expect(host.querySelector('.overlay')).toBeFalsy();
    expect(host.querySelectorAll('p-inputnumber')).toHaveLength(2);
    expect(host.querySelectorAll('button[pbutton].suggestion-chip')).toHaveLength(2);
  });

  it('submits a create payload without a service code', async () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      taxIncluded: true,
      taxRatePercent: 18,
      hsnCode: '9987',
    });

    await component.onSubmit();

    expect(serviceService.addService).toHaveBeenCalledWith({
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    });
  });

  it('rejects invalid submissions', async () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    await component.onSubmit();

    expect(serviceService.addService).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('matches the backend price and description constraints', () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;

    component.form.patchValue({
      name: 'Installation',
      description: 'D'.repeat(1000),
      price: 0,
    });

    expect(component.form.controls.description.valid).toBe(true);
    expect(component.form.controls.price.hasError('min')).toBe(true);
  });

  it('renders specific validation feedback with accessible control descriptions', () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.form.patchValue({
      name: 'N'.repeat(181),
      description: 'D'.repeat(1001),
      price: -1,
      taxRatePercent: 101,
      hsnCode: 'invalid',
    });
    component.form.markAllAsTouched();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('#add-service-name-error')).toBeTruthy();
    expect(host.querySelector('#add-service-description-error')).toBeTruthy();
    expect(host.querySelector('#add-service-price-error')).toBeTruthy();
    expect(host.querySelector('#add-service-tax-error')).toBeTruthy();
    expect(host.querySelector('#add-service-hsn-error')).toBeTruthy();
    expect(
      host.querySelector('input[formcontrolname="name"]')?.getAttribute('aria-describedby'),
    ).toBe('add-service-name-error');
    expect(host.querySelector('input#add-service-price')?.getAttribute('aria-describedby')).toBe(
      'add-service-price-error',
    );
    expect(host.querySelector('input#add-service-price')?.getAttribute('aria-invalid')).toBe(
      'true',
    );
    expect(host.querySelector('input#add-service-tax')?.getAttribute('aria-describedby')).toBe(
      'add-service-tax-error',
    );
    expect(host.querySelector('input#add-service-tax')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('drops invalid semantics from numeric controls once they are valid', () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.form.patchValue({ price: -1, taxRatePercent: 101 });
    component.form.markAllAsTouched();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('input#add-service-price')?.getAttribute('aria-invalid')).toBe(
      'true',
    );

    component.form.patchValue({ price: 250, taxRatePercent: 18 });
    fixture.detectChanges();

    expect(host.querySelector('input#add-service-price')?.getAttribute('aria-invalid')).toBeNull();
    expect(
      host.querySelector('input#add-service-price')?.getAttribute('aria-describedby'),
    ).toBeNull();
    expect(host.querySelector('input#add-service-tax')?.getAttribute('aria-invalid')).toBeNull();
  });

  it('reuses HSN lookup suggestions for the service name', async () => {
    const fixture = TestBed.createComponent(AddServiceOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.controls.name.setValue('Installation');
    component.onNameBlur();
    await Promise.resolve();
    fixture.detectChanges();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Installation');
    expect(component.suggestedHsnCodes()).toEqual(['9987']);
    expect(component.suggestedTaxSlabs()).toEqual(['18%']);
    expect(component.form.controls.hsnCode.value).toBe('9987');
    expect(component.form.controls.taxRatePercent.value).toBe(18);
  });
});
