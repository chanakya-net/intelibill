import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { UserShop } from '../../../core/auth/auth.models';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { stubMemoryStorage } from '../../billing/billing.test-utils';
import { ShopService } from '../../shops/services/shop.service';
import { INTAKE_ROLE_REFUSAL_STORAGE_KEY, IntakeRoleRefusalService } from './intake-role-refusal.service';

describe('IntakeRoleRefusalService', () => {
  const owner = { userId: 'owner-1', shopId: 'shop-1' };
  const membership = (role: string, shopId = owner.shopId): UserShop =>
    ({ shopId, shopName: 'Main', role, isDefault: true, lastUsedAt: null });
  const shops = { getMyShops: vi.fn(() => of<readonly UserShop[]>([membership('Owner')])) };

  function setup(): { service: IntakeRoleRefusalService; scope: ShopScope; context: ShopContextService } {
    TestBed.configureTestingModule({ providers: [{ provide: ShopService, useValue: shops }] });
    const context = TestBed.inject(ShopContextService);
    context.initialize(owner);
    return { service: TestBed.inject(IntakeRoleRefusalService), scope: context.scope()!, context };
  }

  beforeEach(() => {
    stubMemoryStorage();
    shops.getMyShops.mockReset();
    shops.getMyShops.mockReturnValue(of([membership('Owner')]));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('holds a refusal for the tab when the device offers no storage', () => {
    vi.unstubAllGlobals();
    const { service } = setup();

    service.refuse(owner);

    expect(service.isRefused(owner)).toBe(true);
  });

  it('keeps a refusal for the same user and shop across a new application instance', () => {
    setup().service.refuse(owner);
    TestBed.resetTestingModule();

    const { service } = setup();

    expect(service.isRefused(owner)).toBe(true);
    expect(service.isRefused({ ...owner, userId: 'owner-2' })).toBe(false);
    expect(service.isRefused({ ...owner, shopId: 'shop-2' })).toBe(false);
  });

  it.each([
    ['a Staff membership', () => of([membership('Staff')])],
    ['no membership in the shop', () => of([membership('Owner', 'shop-2')])],
    ['a failed read', () => throwError(() => new Error('offline'))],
    ['a malformed answer', () => of(null as unknown as readonly UserShop[])],
  ])('stays refused after %s', async (_case, answer) => {
    const { service, scope } = setup();
    service.refuse(owner);
    shops.getMyShops.mockImplementation(answer);

    expect(await service.revalidate(scope)).toBe(false);

    expect(service.isRefused(owner)).toBe(true);
    expect(service.checking()).toBe(false);
  });

  it.each(['Owner', ' manager '])('lifts the refusal once live membership lists %s', async role => {
    const { service, scope } = setup();
    service.refuse(owner);
    const live = new Subject<readonly UserShop[]>();
    shops.getMyShops.mockReturnValue(live);

    const revalidated = service.revalidate(scope);
    expect(service.checking()).toBe(true);
    expect(service.isRefused(owner)).toBe(true);
    live.next([membership(role)]);
    live.complete();

    expect(await revalidated).toBe(true);
    expect(service.isRefused(owner)).toBe(false);
    expect(localStorage.getItem(INTAKE_ROLE_REFUSAL_STORAGE_KEY)).toBeNull();
  });

  it('ignores an answer for a scope the tab has left', async () => {
    const { service, scope, context } = setup();
    service.refuse(owner);
    const live = new Subject<readonly UserShop[]>();
    shops.getMyShops.mockReturnValue(live);

    const revalidated = service.revalidate(scope);
    context.initialize({ userId: 'owner-2', shopId: owner.shopId });
    live.next([membership('Owner')]);
    live.complete();

    expect(await revalidated).toBe(false);
    expect(service.isRefused(owner)).toBe(true);
  });

  it('does not read membership when nothing was refused', async () => {
    const { service, scope } = setup();

    expect(await service.revalidate(scope)).toBe(false);
    expect(shops.getMyShops).not.toHaveBeenCalled();
  });

  it('follows a refusal recorded or lifted by another tab', () => {
    const { service } = setup();
    const announce = (keys: readonly string[]) => {
      localStorage.setItem(INTAKE_ROLE_REFUSAL_STORAGE_KEY, JSON.stringify(keys));
      window.dispatchEvent(new StorageEvent('storage', { key: INTAKE_ROLE_REFUSAL_STORAGE_KEY }));
    };

    announce(['owner-1|shop-1']);
    expect(service.isRefused(owner)).toBe(true);
    announce([]);
    expect(service.isRefused(owner)).toBe(false);
  });
});
