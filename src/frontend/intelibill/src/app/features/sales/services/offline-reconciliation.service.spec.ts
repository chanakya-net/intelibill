import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import { OFFLINE_RECONCILIATION_ENDPOINTS } from '../../../core/auth/auth.constants';
import { OfflineReconciliationService } from './offline-reconciliation.service';

describe('OfflineReconciliationService', () => {
  function setup(): { service: OfflineReconciliationService; http: HttpTestingController } {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(OfflineReconciliationService), http: TestBed.inject(HttpTestingController) };
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('lists cases with status, page and page size', () => {
    const { service, http } = setup();
    let page: unknown;
    service.listCases('open', 2, 25).subscribe((value) => (page = value));

    const req = http.expectOne((r) => r.url === OFFLINE_RECONCILIATION_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('status')).toBe('open');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ items: [], page: 2, pageSize: 25, totalCount: 0, unresolvedCount: 0 });
    expect(page).toMatchObject({ page: 2 });
  });

  it('gets one case', () => {
    const { service, http } = setup();
    service.getCase('case/1').subscribe();

    const req = http.expectOne(OFFLINE_RECONCILIATION_ENDPOINTS.detail('case/1'));
    expect(req.request.method).toBe('GET');
    expect(req.request.url).toContain('case%2F1');
    req.flush({});
  });

  it('resolves a case with the request body', () => {
    const { service, http } = setup();
    const body = { operationId: 'op-1', expectedVersion: 7, action: 'Escalate' as const, reason: 'Needs owner' };
    service.resolveCase('case-1', body).subscribe();

    const req = http.expectOne(OFFLINE_RECONCILIATION_ENDPOINTS.resolve('case-1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('posts the recovery bundle text byte-for-byte inside the envelope', () => {
    const { service, http } = setup();
    const bundleText = '{"format":"x",  "invoices":[{"a":1.10}]}';
    service.submitRecoveryBundle(bundleText, 'op "1"').subscribe();

    const req = http.expectOne(OFFLINE_RECONCILIATION_ENDPOINTS.recoverySubmissions);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBe('{"operationId":"op \\"1\\"","bundle":' + bundleText + '}');
    expect(req.request.headers.get('Content-Type')).toBe('application/json');
    req.flush({ operationId: 'op "1"', results: [] });
  });
});
