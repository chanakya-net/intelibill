import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { REQUEST_SCOPE } from '../../../core/context/scoped-request';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { SupplierReturnService } from './supplier-return.service';
const owner = { userId: 'captured-user', shopId: 'captured-shop' };
describe('Supplier return API ownership', () => {
  let service: SupplierReturnService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SupplierReturnService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('binds shipment and preview to the captured owner', () => {
    service
      .record(owner, {
        operationKey: 'exact-key',
        supplierId: 'supplier',
        reason: 'Damaged goods',
        creditAccepted: false,
        supplierReference: null,
        lines: [],
      })
      .subscribe();
    const request = http.expectOne(`${API_BASE_URL}/supplier-returns`);
    expect(request.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    expect(request.request.body.operationKey).toBe('exact-key');
    request.flush({});
    service.preview(owner, 'supplier', []).subscribe();
    const preview = http.expectOne(`${API_BASE_URL}/supplier-returns/preview`);
    expect(preview.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    preview.flush({ lines: [] });
  });
  it('binds acceptance and both reversal variants to the captured owner', () => {
    service
      .acceptCredit(owner, 'return', {
        operationKey: 'credit-key',
        expectedVersion: 1,
        reason: 'Agreed credit',
        supplierReference: null,
        lines: [],
      })
      .subscribe();
    service
      .reverseReceipt(owner, 'receipt', {
        operationKey: 'reverse-key',
        reason: 'Wrong receipt',
        expectedReceiptVersion: 1,
        expectedBatchVersion: 2,
      })
      .subscribe();
    service
      .reversePurchaseOrderReceipt(owner, 'group', {
        operationKey: 'group-key',
        reason: 'Wrong delivery',
        lines: [],
      })
      .subscribe();
    const requests = http.match((r) => r.method === 'POST');
    expect(requests).toHaveLength(3);
    for (const request of requests) {
      expect(request.request.context.get(REQUEST_SCOPE)).toEqual(owner);
      request.flush({});
    }
  });
  it('keeps recovery lookup in the original owner and escapes its immutable key', () => {
    service.getOperation(owner, 'supplier-return', 'key / +').subscribe();
    const request = http.expectOne(
      `${API_BASE_URL}/financial-operations/supplier-return/key%20%2F%20%2B`,
    );
    expect(request.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    request.flush({ response: {} });
  });
});
