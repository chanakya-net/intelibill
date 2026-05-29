import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import type { Service } from '../services/service.models';
import { ServiceService } from '../services/service.service';
import { ServicesPageComponent } from './services-page.component';

describe('ServicesPageComponent', () => {
  const servicesSignal = signal<readonly Service[]>([
    {
      serviceId: 'srv-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: null,
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    },
    {
      serviceId: 'srv-2',
      code: 'SRV-0002',
      name: 'Support',
      description: 'Annual support',
      price: 150,
      hsnCode: '9983',
      taxRatePercent: 18,
      taxIncluded: false,
      isActive: false,
    },
  ]);

  const serviceService = {
    getServices: vi.fn(() => of(servicesSignal())),
    activateService: vi.fn(() => of(void 0)),
    deactivateService: vi.fn(() => of(void 0)),
  };

  const roleSignal = signal('Manager');
  const permissionsService = {
    canManageServices: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
  } as ShopPermissionsService;

  beforeEach(() => {
    serviceService.getServices.mockReset().mockImplementation(() => of(servicesSignal()));
    serviceService.activateService.mockReset().mockReturnValue(of(void 0));
    serviceService.deactivateService.mockReset().mockReturnValue(of(void 0));
    roleSignal.set('Manager');
    servicesSignal.set([
      {
        serviceId: 'srv-1',
        code: 'SRV-0001',
        name: 'Installation',
        description: null,
        price: 250,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      },
      {
        serviceId: 'srv-2',
        code: 'SRV-0002',
        name: 'Support',
        description: 'Annual support',
        price: 150,
        hsnCode: '9983',
        taxRatePercent: 18,
        taxIncluded: false,
        isActive: false,
      },
    ]);

    TestBed.configureTestingModule({
      imports: [ServicesPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: ServiceService, useValue: serviceService },
        { provide: ShopPermissionsService, useValue: permissionsService },
      ],
    });
  });

  it('loads services on init', () => {
    TestBed.createComponent(ServicesPageComponent);

    expect(serviceService.getServices).toHaveBeenCalledWith({ search: '', includeInactive: true });
  });

  it('filters and paginates the current list', () => {
    const fixture = TestBed.createComponent(ServicesPageComponent);
    const component = fixture.componentInstance;

    component.services.set(servicesSignal());
    component.statusFilter.set('inactive');
    component.pageSize.set(1);
    component.pageNumber.set(1);

    expect(component.filteredServices()).toHaveLength(1);
    expect(component.filteredServices()[0].serviceId).toBe('srv-2');
    expect(component.pagedServices()).toHaveLength(1);
    expect(component.summary().totalServices).toBe(1);
  });

  it('reloads services for a new search and status filter', () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(ServicesPageComponent);
    const component = fixture.componentInstance;

    component.onSearchChange('support');
    vi.advanceTimersByTime(280);
    component.onStatusFilterChange('active');

    expect(serviceService.getServices).toHaveBeenCalledWith({ search: 'support', includeInactive: false });
    vi.useRealTimers();
    fixture.destroy();
  });

  it('toggles service status from the table actions', () => {
    const fixture = TestBed.createComponent(ServicesPageComponent);
    const component = fixture.componentInstance;
    const activeService = servicesSignal()[0];
    const inactiveService = servicesSignal()[1];

    component.onToggleService(activeService);
    component.onToggleService(inactiveService);

    expect(serviceService.deactivateService).toHaveBeenCalledWith('srv-1');
    expect(serviceService.activateService).toHaveBeenCalledWith('srv-2');
    fixture.destroy();
  });

  it('allows management only for owner and manager roles', () => {
    const fixture = TestBed.createComponent(ServicesPageComponent);
    const component = fixture.componentInstance;

    expect(component.canManageServices()).toBe(true);

    roleSignal.set('Staff');
    expect(component.canManageServices()).toBe(false);
    fixture.destroy();
  });
});
