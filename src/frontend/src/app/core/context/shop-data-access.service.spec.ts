import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SubscriptionStatusService } from '../../features/billing/status/subscription-status.service';
import { ShopUpdatesSignalRService } from '../services/shop-updates-signalr.service';
import { ShopContextService } from './shop-context.service';
import { ShopDataAccessService } from './shop-data-access.service';

describe('ShopDataAccessService', () => {
  afterEach(() => {
    try {
      globalThis.localStorage?.clear?.();
    } catch {
      // Storage may be unavailable in headless test runners.
    }
    TestBed.resetTestingModule();
  });

  it('does not re-read trading authority when setDataRestricted bumps the epoch for the same owner', () => {
    const tradingAuthority = vi.fn().mockReturnValue(EMPTY);
    const authorityChanged$ = new Subject<void>();

    TestBed.configureTestingModule({
      providers: [
        { provide: SubscriptionStatusService, useValue: { tradingAuthority } },
        { provide: ShopUpdatesSignalRService, useValue: { authorityChanged$ } },
      ],
    });

    const context = TestBed.inject(ShopContextService);
    context.initialize({ userId: 'user-1', shopId: 'shop-1' });
    TestBed.inject(ShopDataAccessService);
    TestBed.flushEffects();

    expect(tradingAuthority).toHaveBeenCalledTimes(1);
    expect(tradingAuthority).toHaveBeenLastCalledWith('shop-1');

    const scope = context.scope()!;
    expect(context.setDataRestricted(scope, true)).toBe(true);
    TestBed.flushEffects();

    expect(context.dataRestricted()).toBe(true);
    expect(tradingAuthority).toHaveBeenCalledTimes(1);

    window.dispatchEvent(new Event('focus'));
    expect(tradingAuthority).toHaveBeenCalledTimes(2);
  });
});
