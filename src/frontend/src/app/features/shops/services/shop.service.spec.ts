import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SHOP_ENDPOINTS, BANK_ACCOUNT_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopDetails, ShopService } from './shop.service';

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
      gstNumber: null,
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
      gstNumber: '27AAPFU0939F1ZV',
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
      gstNumber: '27AAPFU0939F1ZV',
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
      gstNumber: '27AAPFU0939F1ZV',
    });

    http.verify();
  });

  it('sends add bank account request and then fetches shop details', () => {
    const { service, http } = setup();

    const shopDetailsResponse: ShopDetails = {
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: null,
      mobileNumber: null,
      gstNumber: null,
      bankName: 'SBI',
      bankAccountNumber: '123456789012',
      bankAccountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Chandra Kumar',
    };

    service.updateBankDetails('shop-1', {
      bankName: 'SBI',
      accountNumber: '123456789012',
      accountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Chandra Kumar',
    }).subscribe((response) => {
      expect(response.bankName).toBe('SBI');
      expect(response.bankAccountNumber).toBe('123456789012');
    });

    const addRequest = http.expectOne(BANK_ACCOUNT_ENDPOINTS.add);
    expect(addRequest.request.method).toBe('POST');
    addRequest.flush({}); // New endpoint returns DTO but switchMap moves to getDetails

    const detailsRequest = http.expectOne(SHOP_ENDPOINTS.details('shop-1'));
    expect(detailsRequest.request.method).toBe('GET');
    detailsRequest.flush(shopDetailsResponse);

    http.verify();
  });
});
