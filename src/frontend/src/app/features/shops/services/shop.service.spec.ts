import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopService } from './shop.service';

describe('ShopService', () => {
  const authService = {
    applyAuthResult: vi.fn<AuthService['applyAuthResult']>(),
  };

  function setup(): { service: ShopService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService },
      ],
    });

    return {
      service: TestBed.inject(ShopService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  beforeEach(() => {
    authService.applyAuthResult.mockReset();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads selected shop details', () => {
    const { service, http } = setup();

    service.getShopDetails('shop-1').subscribe((details) => {
      expect(details.shopId).toBe('shop-1');
      expect(details.name).toBe('Main');
    });

    const request = http.expectOne(SHOP_ENDPOINTS.details('shop-1'));
    expect(request.request.method).toBe('GET');
    request.flush({
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: null,
      mobileNumber: null,
    });

    http.verify();
  });

  it('sends update request for selected shop', () => {
    const { service, http } = setup();

    service.updateShop('shop-1', {
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: 'Chandra',
      mobileNumber: '9876543210',
    }).subscribe((response) => {
      expect(response.shopId).toBe('shop-1');
      expect(response.address).toBe('42 MG Road');
    });

    const request = http.expectOne(SHOP_ENDPOINTS.update('shop-1'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: 'Chandra',
      mobileNumber: '9876543210',
    });
    request.flush({
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: 'Chandra',
      mobileNumber: '9876543210',
    });

    http.verify();
  });
});
