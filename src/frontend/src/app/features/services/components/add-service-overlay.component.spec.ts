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
    addService: vi.fn(() => of({
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: null,
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    })),
  };

  const inventoryService = {
    lookupHsn: vi.fn(() => of({
      hsnCodes: ['9987'],
      taxScenarios: [{ taxPercentage: '18%' }],
    })),
  };

  beforeEach(() => {
    serviceService.addService.mockReset();
    inventoryService.lookupHsn.mockReset();
    TestBed.configureTestingModule({
      imports: [AddServiceOverlayComponent, ReactiveFormsModule, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
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
