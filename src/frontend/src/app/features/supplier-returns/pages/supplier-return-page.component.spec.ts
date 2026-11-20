import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
} from '../../billing/status/operating-access-hint.service';
import type {
  PurchaseReceiptDetail,
  SupplierReturnDetail,
} from '../services/supplier-return.models';
import { SupplierReturnService } from '../services/supplier-return.service';
import {
  SupplierReturnPageComponent,
  validShipmentQuantity,
  validCreditAmount,
} from './supplier-return-page.component';
describe('Supplier return editor', () => {
  it('accepts canonical paise without binary multiplication errors', () => {
    for (const value of [0, 0.29, 1.1, 2.3, 99999999.99])
      expect(validCreditAmount(value)).toBe(true);
    for (const value of [-1, 0.001, 1.101, NaN, Infinity, 100000000])
      expect(validCreditAmount(value)).toBe(false);
  });
  it('limits positive available quantity and item precision', () => {
    expect(validShipmentQuantity(3, 6, 0)).toBe(true);
    expect(validShipmentQuantity(7, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.1, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.125, 6, 3)).toBe(true);
    expect(validShipmentQuantity(0, 6, 3)).toBe(false);
  });
});

describe('Supplier return completion eligibility', () => {
  let page: SupplierReturnPageComponent;
  const shop = 'shop-one';
  const role = signal('Owner');
  const denial = signal<string | null>(null);
  const lot = (overrides: Partial<PurchaseReceiptDetail> = {}) =>
    ({
      supplierId: 'supplier-one',
      returnableQuantity: 4,
      receiptVersion: 1,
      batchVersion: 2,
      reversalEligibilityReasons: [],
      ...overrides,
    }) as PurchaseReceiptDetail;
  const pending = (overrides: Partial<SupplierReturnDetail> = {}) =>
    ({
      status: 'PendingSupplierCredit',
      version: 3,
      lines: [{ returnLineId: 'shipped-line' }],
      ...overrides,
    }) as SupplierReturnDetail;

  beforeEach(() => {
    role.set('Owner');
    denial.set(null);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthService,
          useValue: {
            session: () => ({ activeShopId: shop, shops: [{ shopId: shop, role: role() }] }),
          },
        },
        { provide: OperatingAccessHintService, useValue: { blockingMessage: denial } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
        { provide: Router, useValue: {} },
        { provide: ShopContextService, useValue: {} },
        { provide: SupplierReturnService, useValue: {} },
      ],
    });
    page = TestBed.runInInjectionContext(() => new SupplierReturnPageComponent());
  });

  it('requires versioned source lots and every lot eligible before reversal', () => {
    expect(page.canReverse()).toBe(false);
    page.lots.set([lot(), lot({ batchVersion: 0 })]);
    expect(page.canReverse()).toBe(false);
    page.lots.set([lot(), lot({ reversalEligibilityReasons: ['Payment.DocumentHasAllocations'] })]);
    expect(page.canReverse()).toBe(false);
    expect(page.canRecordShipment()).toBe(true);
    page.lots.set([lot({ returnableQuantity: 0 }), lot({ supplierId: null })]);
    expect(page.canRecordShipment()).toBe(false);
  });

  it('allows owner completion during trading suspension and clears the source under security', () => {
    page.lots.set([lot()]);
    page.document.set(pending());
    denial.set(OPERATING_ACCESS_ERROR_KEYS['Licence.TradingRestricted']);
    expect(page.canRecordShipment()).toBe(true);
    expect(page.canReverse()).toBe(true);
    expect(page.canAcceptCredit()).toBe(true);
    role.set('Manager');
    expect(page.canRecordShipment()).toBe(true);
    expect(page.canReverse()).toBe(false);
    expect(page.canAcceptCredit()).toBe(false);
    denial.set(OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended']);
    expect(page.canRecordShipment()).toBe(false);
    TestBed.tick();
    expect(page.lots()).toEqual([]);
    expect(page.document()).toBeNull();
  });

  it('requires a versioned pending shipment with line identities for credit', () => {
    page.document.set(pending({ lines: [] }));
    expect(page.canAcceptCredit()).toBe(false);
    page.document.set(pending({ version: 0 }));
    expect(page.canAcceptCredit()).toBe(false);
    page.document.set(pending({ status: 'CreditAccepted' }));
    expect(page.canAcceptCredit()).toBe(false);
    page.document.set(pending());
    expect(page.canAcceptCredit()).toBe(true);
  });
});
