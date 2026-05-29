import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SERVICE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { ServiceService } from './service.service';

describe('ServiceService', () => {
  function setup(): { service: ServiceService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(ServiceService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads services with search and inactive filters', () => {
    const { service, http } = setup();

    service.getServices({ search: 'tea', includeInactive: false }).subscribe((services) => {
      expect(services).toHaveLength(1);
      expect(services[0].code).toBe('SRV-0001');
    });

    const request = http.expectOne(
      (req) => req.url === SERVICE_ENDPOINTS.list && req.params.get('search') === 'tea',
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('includeInactive')).toBe('false');

    request.flush([
      {
        serviceId: 'service-1',
        code: 'SRV-0001',
        name: 'Installation',
        description: null,
        price: 250,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      },
    ]);

    http.verify();
  });

  it('creates a service without a code in the payload', () => {
    const { service, http } = setup();

    service
      .addService({
        name: 'Installation',
        description: 'On-site install',
        price: 250,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      })
      .subscribe((createdService) => {
        expect(createdService.code).toBe('SRV-0001');
      });

    const request = http.expectOne(SERVICE_ENDPOINTS.add);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    });

    request.flush({
      serviceId: 'service-1',
      code: 'SRV-0001',
      name: 'Installation',
      description: 'On-site install',
      price: 250,
      hsnCode: '9987',
      taxRatePercent: 18,
      taxIncluded: true,
      isActive: true,
    });

    http.verify();
  });

  it('updates, activates, and deactivates services by id', () => {
    const { service, http } = setup();

    service.updateService('service-1', {
      name: 'Install & Setup',
      description: null,
      price: 300,
      hsnCode: null,
      taxRatePercent: 18,
      taxIncluded: false,
    }).subscribe();

    const updateRequest = http.expectOne(SERVICE_ENDPOINTS.update('service-1'));
    expect(updateRequest.request.method).toBe('PATCH');
    expect(updateRequest.request.body).toEqual({
      name: 'Install & Setup',
      description: null,
      price: 300,
      hsnCode: null,
      taxRatePercent: 18,
      taxIncluded: false,
    });
    updateRequest.flush(null);

    service.activateService('service-1').subscribe();
    const activateRequest = http.expectOne(SERVICE_ENDPOINTS.activate('service-1'));
    expect(activateRequest.request.method).toBe('POST');
    activateRequest.flush(null);

    service.deactivateService('service-1').subscribe();
    const deactivateRequest = http.expectOne(SERVICE_ENDPOINTS.deactivate('service-1'));
    expect(deactivateRequest.request.method).toBe('POST');
    deactivateRequest.flush(null);

    http.verify();
  });
});
