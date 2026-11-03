import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AccessScopeResult } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopDetails, ShopService } from './shop.service';

describe('ShopService', () => {
  const authService = {
    applyAuthResult: vi.fn<AuthService['applyAuthResult']>(),
    applyAccessScope: vi.fn<AuthService['applyAccessScope']>(),
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
    authService.applyAccessScope.mockReset();
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

  it('switches the active shop and adopts the reissued access scope', () => {
    const { service, http } = setup();

    const scope: AccessScopeResult = {
      accessToken: 'token-for-shop-2',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      activeShopId: 'shop-2',
      scopeVersion: 4,
      shops: [
        { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
        { shopId: 'shop-2', shopName: 'Second', role: 'Manager', isDefault: false, lastUsedAt: null },
      ],
    };

    service.switchActiveShop('shop-2').subscribe();

    const request = http.expectOne(SHOP_ENDPOINTS.switch);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ shopId: 'shop-2' });
    request.flush(scope);

    expect(authService.applyAccessScope).toHaveBeenCalledWith(scope);

    http.verify();
  });

  it('sends selected shop bank details update request', () => {
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

    const request = http.expectOne(`${SHOP_ENDPOINTS.details('shop-1')}/bank-details`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      bankName: 'SBI',
      accountNumber: '123456789012',
      accountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Chandra Kumar',
    });
    request.flush(shopDetailsResponse);

    http.verify();
  });

  it('declares the shop tax profile and returns the saved details', () => {
    const { service, http } = setup();
    const saved: ShopDetails = {
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Pune',
      state: 'Maharashtra',
      pincode: '411001',
      contactPerson: null,
      mobileNumber: null,
      gstNumber: '27AAPFU0939F1ZV',
      bankName: null,
      bankAccountNumber: null,
      bankAccountType: null,
      ifscCode: null,
      accountHolderName: null,
      taxProfile: {
        legalName: 'Main Traders LLP',
        stateCode: '27',
        taxRegime: 'Regular',
        invoiceSeriesCode: 'B',
        eInvoiceStatus: 'BelowThreshold',
        eInvoiceAttestedAt: '2026-11-03T05:30:00Z',
      },
    };

    service
      .updateTaxProfile('shop-1', {
        legalName: 'Main Traders LLP',
        stateCode: '27',
        taxRegime: 'Regular',
        gstNumber: '27AAPFU0939F1ZV',
        invoiceSeriesCode: 'B',
        eInvoiceStatus: 'BelowThreshold',
      })
      .subscribe((response) => {
        expect(response.taxProfile?.taxRegime).toBe('Regular');
        expect(response.taxProfile?.invoiceSeriesCode).toBe('B');
      });

    const request = http.expectOne(SHOP_ENDPOINTS.taxProfile('shop-1'));
    expect(request.request.url).toBe(`${SHOP_ENDPOINTS.details('shop-1')}/tax-profile`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      legalName: 'Main Traders LLP',
      stateCode: '27',
      taxRegime: 'Regular',
      gstNumber: '27AAPFU0939F1ZV',
      invoiceSeriesCode: 'B',
      eInvoiceStatus: 'BelowThreshold',
    });
    request.flush(saved);

    http.verify();
  });

  it('sends the tax profile with a new shop', () => {
    const { service, http } = setup();

    service
      .createShop({
        name: 'Main',
        address: '42 MG Road',
        city: 'Pune',
        state: 'Maharashtra',
        pincode: '411001',
        gstNumber: '27AAPFU0939F1ZV',
        taxProfile: {
          legalName: null,
          stateCode: '27',
          taxRegime: 'Composition',
          invoiceSeriesCode: null,
          eInvoiceStatus: null,
        },
      })
      .subscribe();

    const request = http.expectOne(SHOP_ENDPOINTS.create);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.taxProfile).toEqual({
      legalName: null,
      stateCode: '27',
      taxRegime: 'Composition',
      invoiceSeriesCode: null,
      eInvoiceStatus: null,
    });
    expect(request.request.body.gstNumber).toBe('27AAPFU0939F1ZV');
    request.flush({ accessToken: 'token', expiresAt: '2026-11-03T06:00:00Z', activeShopId: 'shop-1' });

    http.verify();
  });
});
