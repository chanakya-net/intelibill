import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { ExpiryPolicyService } from './expiry-policy.service';
import { expiryPolicyState } from '../../../../tests/expiry/expiry-policy.providers';
function setup(role: string) {
  const owner = { userId: 'user', shopId: 'shop', epoch: 1 };
  let stale = false;
  const session = signal({
    user: { id: 'user' },
    activeShopId: 'shop',
    shops: [{ shopId: 'shop', role }],
  });
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { session } },
      {
        provide: ShopContextService,
        useValue: { scope: () => owner, epoch: () => 1, isCurrent: () => true },
      },
      {
        provide: OfflineSalesSnapshotIndexedDbService,
        useValue: {
          getUsableSnapshotInfo: async () => (stale ? null : { catalogVersion: 1 }),
          invalidateForShop: async () => {
            stale = true;
          },
        },
      },
    ],
  });
  return {
    service: TestBed.inject(ExpiryPolicyService),
    http: TestBed.inject(HttpTestingController),
    stale: () => stale,
    session,
  };
}
describe('expiry policy authority and future cache', () => {
  it('refuses Staff and Manager writes before HTTP', async () => {
    const { service, http, session } = setup('Staff');
    const request = { expectedCatalogVersion: 1, policy: null, reason: 'Review' };
    await expect(firstValueFrom(service.update(request, 'item'))).rejects.toThrow(
      'expiry.ownerOnly',
    );
    session.update((s) => ({ ...s, shops: [{ shopId: 'shop', role: 'Manager' }] }));
    await expect(firstValueFrom(service.update(request, 'item'))).rejects.toThrow(
      'expiry.ownerOnly',
    );
    http.expectNone((x) => x.method === 'PUT');
    http.verify();
  });
  it('sends owner reason/version and inheritance without smuggling ordinary item fields', async () => {
    const { service, http, stale } = setup('Owner');
    const done = firstValueFrom(
      service.update({ expectedCatalogVersion: 1, policy: null, reason: 'Use shop rule' }, 'item'),
    );
    const request = http.expectOne((x) => x.method === 'PUT');
    expect(request.request.url).toMatch(/expiry-policies\/items\/item$/);
    expect(request.request.body).toEqual({
      expectedCatalogVersion: 1,
      policy: null,
      reason: 'Use shop rule',
    });
    request.flush({ ...expiryPolicyState, catalogVersion: 2 });
    await done;
    expect(stale()).toBe(true);
    http.verify();
  });
  it('invalidates future catalog use after a policy write with an unknown server outcome', async () => {
    const { service, http, stale } = setup('Owner');
    const done = firstValueFrom(
      service.update({ expectedCatalogVersion: 1, policy: null, reason: 'Use shop rule' }, 'item'),
    ).catch((x) => x);
    http
      .expectOne((x) => x.method === 'PUT')
      .flush({}, { status: 503, statusText: 'Unknown outcome' });
    await done;
    expect(stale()).toBe(true);
    http.verify();
  });
  it('detects a connected remote catalog revision on policy refresh and invalidates future use', async () => {
    const { service, http, stale } = setup('Owner');
    const observed: string[] = [];
    service.changed$.subscribe((x) => observed.push(x));
    const done = firstValueFrom(service.read());
    http.expectOne((x) => x.method === 'GET').flush({ ...expiryPolicyState, catalogVersion: 2 });
    await done;
    await vi.waitFor(() => expect(stale()).toBe(true));
    expect(observed).toEqual(['shop']);
    http.verify();
  });
});
