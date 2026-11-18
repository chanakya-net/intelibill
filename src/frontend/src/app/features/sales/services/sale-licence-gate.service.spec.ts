import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { SaleLicenceGateService } from './sale-licence-gate.service';

describe('SaleLicenceGateService', () => {
  const owner = { userId: 'cashier', shopId: 'shop-1' };
  let gate: SaleLicenceGateService;
  let context: ShopContextService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: AppReloadService, useValue: { restartAtAppRoot: () => {} } },
      ],
    });
    context = TestBed.inject(ShopContextService);
    context.initialize(owner);
    gate = TestBed.inject(SaleLicenceGateService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const request = (controller: HttpTestingController) =>
    controller.expectOne((req) => req.url.endsWith('/shop-1/trading-authority'));

  it.each([
    [403, { code: 'Licence.Expired', enforced: true }, 'expired'],
    [503, { code: 'Licence.VerificationUnavailable', enforced: true }, 'unavailable'],
    [403, { code: 'Licence.Expired', enforced: false }, null],
    [404, { code: 'Licence.Expired', enforced: true }, null],
    [503, { enforced: true }, null],
    [200, { schemaVersion: 1 }, null],
  ] as const)('uses the enforced decision in HTTP %s', async (status, body, key) => {
    const reading = gate.refresh();
    request(http).flush(body, { status, statusText: key ?? 'Fixture' });
    await reading;
    expect(gate.blockingMessage()).toBe(key ? `billing.status.tradingAuthority.${key}` : null);
  });

  it('retains a POST decision through transport loss and accepts a fresh grant', async () => {
    const stale = gate.refresh();
    const staleRequest = request(http);
    gate.reportRefusal(new HttpErrorResponse({ status: 403, error: { code: 'Licence.Expired' } }), context.scope());
    const grant = { schemaVersion: 1, shopId: owner.shopId, enforced: true,
      authorityVerifiedAt: '2026-11-18T00:00:00Z', validUntil: '2026-11-19T00:00:00Z' };
    staleRequest.flush(grant);
    expect(await stale).toBe(false);
    expect(gate.blockingMessage()).toBe('billing.status.tradingAuthority.expired');

    const unavailable = gate.refresh();
    request(http).error(new ProgressEvent('network error'));
    expect(await unavailable).toBe(false);
    expect(gate.blockingMessage()).toBe('billing.status.tradingAuthority.expired');

    const renewed = gate.refresh();
    request(http).flush(grant);
    expect(await renewed).toBe(true);
    expect(gate.blockingMessage()).toBeNull();
  });

  it('discards a denial from the previous shop epoch even after returning to that shop', async () => {
    const oldScope = context.scope();
    const reading = gate.refresh();
    const oldRequest = request(http);
    context.transition({ ...owner, shopId: 'shop-2' }, 'scope', () => {});
    context.transition(owner, 'scope', () => {});
    oldRequest.flush({ code: 'Licence.Expired', enforced: true }, { status: 403, statusText: 'Expired' });
    expect(await reading).toBe(false);
    gate.reportRefusal(new HttpErrorResponse({ status: 403, error: { code: 'Licence.Expired' } }), oldScope);
    expect(gate.blockingMessage()).toBeNull();
  });
});
