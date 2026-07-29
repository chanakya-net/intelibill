import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { InventoryService } from '../../inventory/services/inventory.service';
import { ServiceService } from '../services/service.service';
import { EditServiceOverlayComponent } from './edit-service-overlay.component';

describe('EditServiceOverlayComponent', () => {
  const serviceService = {
    updateService: vi.fn(() => of(void 0)),
  };

  const inventoryService = {
    lookupHsn: vi.fn(() =>
      of({
        hsnCodes: ['9983'],
        taxScenarios: [{ taxPercentage: '18%' }],
      }),
    ),
  };

  beforeEach(() => {
    serviceService.updateService.mockClear();
    inventoryService.lookupHsn.mockClear();
    TestBed.configureTestingModule({
      imports: [
        EditServiceOverlayComponent,
        ReactiveFormsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: ServiceService, useValue: serviceService },
        { provide: InventoryService, useValue: inventoryService },
      ],
    });
  });

  it('initializes the form with service values and read-only code', () => {
    const fixture = TestBed.createComponent(EditServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.service = {
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    };
    component.ngOnInit();
    fixture.detectChanges();

    expect(component.form.value).toEqual({
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      taxIncluded: true,
      taxRatePercent: 18,
      hsnCode: '9987',
    });

    const codeInput = fixture.nativeElement.querySelector('input[readonly]');
    expect(codeInput.value).toBe('SRV-0001');
  });

  it('renders through the shared PrimeNG editor dialog controls', async () => {
    const fixture = TestBed.createComponent(EditServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.service = {
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    };
    component.ngOnInit();
    fixture.detectChanges();

    component.form.controls.name.setValue('Support plan');
    component.onNameBlur();
    await Promise.resolve();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('p-dialog')).toBeTruthy();
    expect(host.querySelector('.overlay')).toBeFalsy();
    expect(host.querySelectorAll('p-inputnumber')).toHaveLength(2);
    expect(host.querySelectorAll('button[pbutton].suggestion-chip')).toHaveLength(2);
  });

  it('submits an update payload without changing the code', async () => {
    const fixture = TestBed.createComponent(EditServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.service = {
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: null,
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    };
    component.ngOnInit();
    fixture.detectChanges();

    component.form.patchValue({
      name: 'Install & Setup',
      description: 'Updated',
      price: 300,
      taxIncluded: false,
      taxRatePercent: 12,
      hsnCode: '9983',
    });

    await component.onSubmit();

    expect(serviceService.updateService).toHaveBeenCalledWith('srv-1', {
      name: 'Install & Setup',
      description: 'Updated',
      price: 300,
      hsnCode: '9983',
      taxRatePercent: 12,
      taxIncluded: false,
    });
  });

  it('renders validation feedback for every blocking edit field', () => {
    const fixture = TestBed.createComponent(EditServiceOverlayComponent);
    const component = fixture.componentInstance;
    component.service = {
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: null,
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    };
    fixture.detectChanges();
    component.form.patchValue({
      name: '',
      description: 'D'.repeat(321),
      price: -1,
      taxRatePercent: 101,
      hsnCode: 'invalid',
    });
    component.form.markAllAsTouched();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('.field-error')).toHaveLength(5);
    expect(
      host.querySelector('input[formcontrolname="hsnCode"]')?.getAttribute('aria-describedby'),
    ).toBe('edit-service-hsn-error');
    expect(host.querySelector('input#edit-service-tax')?.getAttribute('aria-describedby')).toBe(
      'edit-service-tax-error',
    );
  });
});
