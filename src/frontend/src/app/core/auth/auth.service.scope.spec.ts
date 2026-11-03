import { describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';

import { AccessScopeResult } from './auth.models';
import { buildSession, createAuthServiceTestHarness, FakeBroadcastChannel } from './auth.service.test-utils';
import { readScopeVersion } from './auth-session.service';
import { ShopContextService } from '../context/shop-context.service';
import { ScopeChange } from '../context/shop-scope';

const shops = [
  { shopId: 'shop-a', shopName: 'A', role: 'Owner', isDefault: true, lastUsedAt: null },
  { shopId: 'shop-b', shopName: 'B', role: 'Staff', isDefault: false, lastUsedAt: null },
];

function jwtWithScopeVersion(version: number): string {
  const encode = (value: object): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256' })}.${encode({ sub: 'user-1', scope_version: version })}.signature`;
}

function scopeResult(overrides?: Partial<AccessScopeResult>): AccessScopeResult {
  return {
    accessToken: 'shop-b-access-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    activeShopId: 'shop-b',
    scopeVersion: 6,
    shops,
    ...overrides,
  };
}

describe('AuthService scope transitions', () => {
  const t = createAuthServiceTestHarness();

  function setupInShopA(overrides?: Parameters<typeof buildSession>[0]) {
    t.storage.loadSession.mockReturnValue(
      buildSession({ activeShopId: 'shop-a', shops, sessionId: 'session-1', ...overrides }),
    );
    const harness = t.setup();
    const context = TestBed.inject(ShopContextService);
    const changes: ScopeChange[] = [];
    context.onDetach((change) => {
      changes.push(change);
    });
    return { ...harness, context, changes };
  }

  it('scope_result_retains_refresh_secret: a switch keeps the refresh credential and moves the tab once', () => {
    const { service, context, changes } = setupInShopA();

    expect(service.applyAccessScope(scopeResult())).toBe(true);

    const session = service.session();
    expect(session?.refreshToken).toBe('refresh-token');
    expect(session?.accessToken).toBe('shop-b-access-token');
    expect(session?.activeShopId).toBe('shop-b');
    expect(changes).toHaveLength(1);
    expect(changes[0]).toMatchObject({ previous: { shopId: 'shop-a' }, next: { shopId: 'shop-b' }, reason: 'scope' });
    expect(context.epoch()).toBe(1);
    expect(t.appReload.restartAtAppRoot).toHaveBeenCalledTimes(1);
  });

  it('refuses a scope answer older than the scope this session already holds', () => {
    const { service, changes } = setupInShopA({ accessToken: jwtWithScopeVersion(7) });

    expect(service.applyAccessScope(scopeResult({ scopeVersion: 6 }))).toBe(false);

    expect(service.session()?.activeShopId).toBe('shop-a');
    expect(changes).toHaveLength(0);
    expect(t.appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('refuses a scope answer requested from another device session', () => {
    const { service, changes } = setupInShopA();

    const accepted = service.applyAccessScope(scopeResult(), { userId: 'user-1', sessionId: 'session-0' });

    expect(accepted).toBe(false);
    expect(service.session()?.activeShopId).toBe('shop-a');
    expect(changes).toHaveLength(0);
  });

  it('default_preference_does_not_change_active_scope', () => {
    const { service, context, changes } = setupInShopA();
    const tokenBefore = service.getAccessToken();

    service.applyShopPreference({
      activeShopId: 'shop-a',
      shops: shops.map((shop) => ({ ...shop, isDefault: shop.shopId === 'shop-b' })),
    });

    expect(service.session()?.activeShopId).toBe('shop-a');
    expect(service.getAccessToken()).toBe(tokenBefore);
    expect(service.session()?.shops.find((shop) => shop.isDefault)?.shopId).toBe('shop-b');
    expect(context.epoch()).toBe(0);
    expect(changes).toHaveLength(0);
    expect(t.appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('cross_tab_scope_change_follows_transition: another tab moving the session moves this tab', () => {
    const { service, context, changes } = setupInShopA();
    const otherTab = new FakeBroadcastChannel('intelibill.auth.session');

    otherTab.postMessage({
      type: 'SESSION_UPDATED',
      session: buildSession({ activeShopId: 'shop-b', shops, accessToken: 'other-tab-token' }),
    });

    expect(changes).toHaveLength(1);
    expect(changes[0]).toMatchObject({ previous: { shopId: 'shop-a' }, next: { shopId: 'shop-b' }, reason: 'cross-tab' });
    expect(service.getAccessToken()).toBe('other-tab-token');
    expect(context.transitioning()).toBe(true);
    expect(t.appReload.restartAtAppRoot).toHaveBeenCalledTimes(1);
  });

  it('adopts another tab renewing the same scope without moving this one', () => {
    const { service, context, changes } = setupInShopA();
    const otherTab = new FakeBroadcastChannel('intelibill.auth.session');

    otherTab.postMessage({
      type: 'SESSION_UPDATED',
      session: buildSession({ activeShopId: 'shop-a', shops, accessToken: 'renewed-token' }),
    });

    expect(service.getAccessToken()).toBe('renewed-token');
    expect(changes).toHaveLength(0);
    expect(context.epoch()).toBe(0);
    expect(t.appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('detaches the signed-in scope on sign-out, mirrored from another tab', () => {
    const { service, changes } = setupInShopA();
    const otherTab = new FakeBroadcastChannel('intelibill.auth.session');

    otherTab.postMessage({ type: 'SESSION_CLEARED' });

    expect(service.session()).toBeNull();
    expect(changes).toEqual([
      expect.objectContaining({ previous: expect.objectContaining({ shopId: 'shop-a' }), next: null }),
    ]);
    expect(t.router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(t.appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('runs the detach hooks while the old session is still the one installed', () => {
    const { service, context } = setupInShopA();
    const seen = vi.fn();
    context.onDetach(() => seen(service.session()?.activeShopId));

    service.applyAccessScope(scopeResult());

    expect(seen).toHaveBeenCalledWith('shop-a');
  });
});

describe('readScopeVersion', () => {
  it('reads the scope_version claim and ignores tokens without one', () => {
    expect(readScopeVersion(jwtWithScopeVersion(12))).toBe(12);
    expect(readScopeVersion('opaque-token')).toBeNull();
    expect(readScopeVersion('a.not-json.c')).toBeNull();
  });
});
