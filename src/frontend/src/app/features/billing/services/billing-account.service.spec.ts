import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { BillingAccount, CreatedBillingAccount } from './billing-account.models';
import { BillingAccountService } from './billing-account.service';

describe('BillingAccountService', () => {
  const accounts = `${API_BASE_URL}/billing/accounts`;
  const account: BillingAccount = {
    accountId: '4b3a2a46-5f3b-8c1f-9d64-6b0a2b1c3d4e',
    name: 'Acme Billing',
    role: 'Owner',
    createdAt: '2026-11-15T10:00:00+00:00',
  };
  let service: BillingAccountService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(BillingAccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the signed-in person’s billing accounts', () => {
    let listed: readonly BillingAccount[] = [];
    service.list().subscribe((value) => (listed = value));

    const request = http.expectOne(accounts);
    expect(request.request.method).toBe('GET');
    request.flush({ accounts: [account] });
    expect(listed).toEqual([account]);
  });

  it('traverses bounded pages before publishing a complete refreshed list', () => {
    let listed: readonly BillingAccount[] = [];
    const later = { ...account, accountId: 'ffffffff-ffff-8fff-9fff-ffffffffffff', name: 'New account' };
    service.list().subscribe((value) => (listed = value));
    http.expectOne(accounts).flush({ accounts: [account], nextCursor: account.accountId });
    expect(listed).toEqual([]);
    http.expectOne(`${accounts}?after=${account.accountId}`).flush({ accounts: [later], nextCursor: null });
    expect(listed).toEqual([account, later]);
  });

  it('does not publish a truncated list when a later page fails', () => {
    let emitted = false;
    let failed = false;
    service.list().subscribe({ next: () => (emitted = true), error: () => (failed = true) });
    http.expectOne(accounts).flush({ accounts: [account], nextCursor: account.accountId });
    http.expectOne(`${accounts}?after=${account.accountId}`).flush({}, { status: 503, statusText: 'Unavailable' });
    expect(emitted).toBe(false);
    expect(failed).toBe(true);
  });

  it('sends exactly the frozen operation key and name, and reports first creation', () => {
    let result: CreatedBillingAccount | null = null;
    service
      .create({ operationKey: 'key-1', name: 'Acme Billing' })
      .subscribe((value) => (result = value));

    const request = http.expectOne(accounts);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ operationKey: 'key-1', name: 'Acme Billing' });
    request.flush(account, { status: 201, statusText: 'Created' });
    expect(result).toEqual({ account, created: true });
  });

  it('reports a completed retry as not newly created', () => {
    let result: CreatedBillingAccount | null = null;
    service.create({ operationKey: 'key-1', name: 'Acme Billing' }).subscribe((value) => (result = value));

    http.expectOne(accounts).flush(account, { status: 200, statusText: 'OK' });
    expect(result).toEqual({ account, created: false });
  });
});
