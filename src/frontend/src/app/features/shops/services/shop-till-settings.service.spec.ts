import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { REQUEST_SCOPE, ScopeMismatchError } from '../../../core/context/scoped-request';
import { TillSettingsEvents } from '../../tills/services/till-settings-events.service';
import { ShopTillSettingsService } from './shop-till-settings.service';

describe('ShopTillSettingsService', () => {
  const scope = signal({ userId: 'owner', shopId: 'shop-a', epoch: 1 });
  const session = signal<any>({
    user: { id: 'owner' },
    activeShopId: 'shop-a',
    shops: [{ shopId: 'shop-a', role: 'Owner' }],
  });
  const release = vi.fn();
  const publish = vi.fn();
  let api: ShopTillSettingsService;
  let http: HttpTestingController;
  beforeEach(() => {
    vi.clearAllMocks();
    scope.set({ userId: 'owner', shopId: 'shop-a', epoch: 1 });
    session.set({
      user: { id: 'owner' },
      activeShopId: 'shop-a',
      shops: [{ shopId: 'shop-a', role: 'Owner' }],
    });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { session } },
        { provide: ShopContextService, useValue: { scope, beginMutation: () => ({ release }) } },
        { provide: TillSettingsEvents, useValue: { publish } },
      ],
    });
    api = TestBed.inject(ShopTillSettingsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  it('scopes a versioned owner update and invalidates capability only after the response', () => {
    const payload = { enabled: true, expectedVersion: 0, reason: 'Start counts', confirmed: true };
    api.update('shop-a', payload).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/shops/shop-a/till-settings'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    expect(req.request.context.get(REQUEST_SCOPE)).toEqual({ userId: 'owner', shopId: 'shop-a' });
    expect(publish).not.toHaveBeenCalled();
    req.flush({ shopId: 'shop-a', available: true, enabled: true, version: 1 });
    expect(publish).toHaveBeenCalledWith('shop-a');
    expect(release).toHaveBeenCalledOnce();
  });
  it('checks scope at subscription time and never sends an old-shop update', () => {
    const pending = api.update('shop-a', {
      enabled: true,
      expectedVersion: 0,
      reason: 'Counts',
      confirmed: true,
    });
    scope.set({ userId: 'owner', shopId: 'shop-b', epoch: 2 });
    let error: unknown;
    pending.subscribe({ error: (e) => (error = e) });
    expect(error).toBeInstanceOf(ScopeMismatchError);
    http.expectNone((r) => r.method === 'PUT');
  });
  it('refuses a manager write before HTTP and releases a failed owner request without announcing success', () => {
    session.set({ ...session(), shops: [{ shopId: 'shop-a', role: 'Manager' }] });
    api
      .update('shop-a', { enabled: true, expectedVersion: 0, reason: 'Counts', confirmed: true })
      .subscribe({ error: () => {} });
    http.expectNone((r) => r.method === 'PUT');
    session.set({ ...session(), shops: [{ shopId: 'shop-a', role: 'Owner' }] });
    api
      .update('shop-a', { enabled: true, expectedVersion: 0, reason: 'Counts', confirmed: true })
      .subscribe({ error: () => {} });
    http.expectOne((r) => r.method === 'PUT').error(new ProgressEvent('offline'));
    expect(release).toHaveBeenCalledOnce();
    expect(publish).not.toHaveBeenCalled();
  });
});
