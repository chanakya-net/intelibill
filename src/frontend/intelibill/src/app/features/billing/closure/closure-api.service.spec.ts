import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/interceptors/auth.interceptor';
import { ClosureApi, type ClosureCommand } from './closure-api.service';

it('authenticates all closure routes on the configured development API', async () => {
  expect(API_BASE_URL).toBe('http://localhost:5277/api');
  TestBed.configureTestingModule({ providers: [
    provideHttpClient(withInterceptors([authInterceptor])),
    provideHttpClientTesting(),
    { provide: AuthService, useValue: { getAccessToken: () => 'owner-token', session: () => null } },
    { provide: Router, useValue: {} },
  ] });
  const api = TestBed.inject(ClosureApi);
  const http = TestBed.inject(HttpTestingController);
  const command: ClosureCommand = {
    operationKey: 'key', expectedVersion: 0, ownerVersion: 1, workDigest: 'a'.repeat(64),
    reviewedKnownWork: true, consideredDeviceQueues: true, financialTreatment: 'NoFinancialChange',
  };
  const shop = '/licensing/shops/shop';
  const account = '/billing/accounts/account/closures';
  const cases = [
    ['GET', shop + '/closure-preview', () => api.review('shop')],
    ['POST', shop + '/closure', () => api.close('shop', command)],
    ['GET', shop + '/account-closure', () => api.ownerProposal('shop')],
    ['POST', shop + '/account-closure/proposal', () => api.consent('shop', 'proposal', command)],
    ['GET', account, () => api.accountProposal('account')],
    ['POST', account, () => api.propose('account', 'key')],
    ['POST', account + '/proposal/confirm', () => api.confirm('account', 'proposal', {
      operationKey: 'key', scopeDigest: 'b'.repeat(64), confirmed: true,
    })],
  ] as const;

  for (const [method, path, send] of cases) {
    const outcome = send();
    const request = http.expectOne({ method, url: API_BASE_URL + path });
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-token');
    request.flush({});
    await outcome;
  }
  http.verify();
});
