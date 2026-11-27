import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { REQUEST_SCOPE, StaleScopeError } from '../../../core/context/scoped-request';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { ShopSetupService } from './shop-setup.service';

const owner = { shopId: 'shop-a', userId: 'owner' };
const nextShop = { ...owner, shopId: 'shop-b' };
const prefs = { version: 0, showOnStartup: true, sampleReceiptAcknowledgedAt: null };

describe(ShopSetupService.name, () => {
  let context: ShopContextService;
  let http: HttpTestingController;
  let service: ShopSetupService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
      ],
    });
    context = TestBed.inject(ShopContextService);
    context.initialize(owner);
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ShopSetupService);
  });

  it('cancels an old-shop read and rejects an A → B → A result epoch', async () => {
    const original = context.scope()!;
    const show = vi.fn();
    service.getStatus(original).subscribe(show);
    const request = http.expectOne(SHOP_ENDPOINTS.setup(owner.shopId));
    context.transition(nextShop, 'scope', () => undefined);
    expect(request.cancelled).toBe(true);
    expect(show).not.toHaveBeenCalled();

    context.transition(owner, 'scope', () => undefined);
    await expect(firstValueFrom(service.getStatus(original))).rejects.toBeInstanceOf(StaleScopeError);
    http.verify();
  });

  it('cancels a sample load on sign-out', () => {
    const show = vi.fn();
    service.getSample(context.scope()!).subscribe(show);
    const request = http.expectOne(`${SHOP_ENDPOINTS.setup(owner.shopId)}/sample`);
    context.transition(null, 'sign-out', () => undefined);
    expect(request.cancelled).toBe(true);
    expect(show).not.toHaveBeenCalled();
    http.verify();
  });

  it('keeps a preference write scoped and blocks switching until it settles', async () => {
    const pending = firstValueFrom(service.save(context.scope()!, {
      expectedVersion: 0, showOnStartup: false, acknowledgeSample: false,
    }));
    const request = http.expectOne(`${SHOP_ENDPOINTS.setup(owner.shopId)}/preferences`);
    expect(request.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    expect(request.request.body.expectedVersion).toBe(0);
    await expect(context.prepareToLeave()).resolves.toEqual({ ok: false, reason: 'mutation-in-flight' });
    request.flush({ ...prefs, version: 1, showOnStartup: false });
    await expect(pending).resolves.toMatchObject({ version: 1, showOnStartup: false });
    await expect(context.prepareToLeave()).resolves.toEqual({ ok: true });
    http.verify();
  });
});
