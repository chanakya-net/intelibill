import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { AppReloadService } from '../services/app-reload.service';
import { ShopContextActions, scopeResetMetaReducer } from './shop-context.actions';
import { RESTART_SAVE_WAIT_MS, ShopContextService } from './shop-context.service';
import { ScopeChange } from './shop-scope';

const A = { userId: 'user-1', shopId: 'shop-a' };
const B = { userId: 'user-1', shopId: 'shop-b' };

describe('ShopContextService', () => {
  const appReload = { restartAtAppRoot: vi.fn<AppReloadService['restartAtAppRoot']>() };
  const store = { dispatch: vi.fn() };

  beforeEach(() => {
    appReload.restartAtAppRoot.mockReset();
    store.dispatch.mockReset();
    TestBed.configureTestingModule({
      providers: [
        { provide: AppReloadService, useValue: appReload },
        { provide: Store, useValue: store },
      ],
    });
  });

  function create(initial: typeof A | null = A): ShopContextService {
    const service = TestBed.inject(ShopContextService);
    service.initialize(initial);
    return service;
  }

  it('installs credentials without moving the epoch when the owner stays the same', () => {
    const service = create();
    const install = vi.fn();
    const detach = vi.fn();
    service.onDetach(detach);

    const change = service.transition({ ...A }, 'refresh', install);

    expect(change).toBeNull();
    expect(install).toHaveBeenCalledTimes(1);
    expect(detach).not.toHaveBeenCalled();
    expect(service.epoch()).toBe(0);
    expect(store.dispatch).not.toHaveBeenCalled();
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('detaches and resets the old scope before the new session is installed, then restarts', () => {
    const service = create();
    const order: string[] = [];
    service.onDetach((change) => {
      order.push(`detach:${change.previous?.shopId}->${change.next?.shopId}`);
    });
    store.dispatch.mockImplementation(() => order.push('reset'));
    appReload.restartAtAppRoot.mockImplementation(() => order.push('restart'));

    const change = service.transition(B, 'scope', () => {
      order.push(`install:scope=${service.scope()?.shopId}`);
    });

    expect(order).toEqual(['detach:shop-a->shop-b', 'reset', 'install:scope=shop-b', 'restart']);
    expect(change?.next).toEqual({ ...B, epoch: 1 });
    expect(service.epoch()).toBe(1);
    expect(service.transitioning()).toBe(true);
    expect(store.dispatch).toHaveBeenCalledWith(ShopContextActions.scopeChanged({ epoch: 1, reason: 'scope' }));
  });

  it('restarts only once the old owner\'s drafts are saved', async () => {
    const service = create();
    let finishSave!: () => void;
    service.onDetach(() => new Promise<void>((resolve) => (finishSave = resolve)));

    service.transition(B, 'cross-tab', () => undefined);
    await Promise.resolve();
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();

    finishSave();
    await vi.waitFor(() => expect(appReload.restartAtAppRoot).toHaveBeenCalledTimes(1));
  });

  it('restarts after the wait limit when a save never finishes', async () => {
    vi.useFakeTimers();
    try {
      const service = create();
      service.onDetach(() => new Promise<void>(() => undefined));

      service.transition(B, 'cross-tab', () => undefined);
      await vi.advanceTimersByTimeAsync(RESTART_SAVE_WAIT_MS - 1);
      expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1);
      expect(appReload.restartAtAppRoot).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('signs out without restarting and signs in again without hiding content', () => {
    const service = create();

    service.transition(null, 'sign-out', () => undefined);
    expect(service.scope()).toBeNull();
    expect(service.transitioning()).toBe(true);
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();

    service.transition({ userId: 'user-2', shopId: 'shop-a' }, 'sign-in', () => undefined);
    expect(service.transitioning()).toBe(false);
    expect(service.epoch()).toBe(2);
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });

  it('keeps installing the new scope when a detach hook throws', () => {
    const service = create();
    const install = vi.fn();
    service.onDetach(() => {
      throw new Error('hook failed');
    });
    const later = vi.fn<(change: ScopeChange) => void>();
    service.onDetach(later);

    service.transition(B, 'cross-tab', install);

    expect(install).toHaveBeenCalledTimes(1);
    expect(later).toHaveBeenCalledTimes(1);
    expect(service.isCurrent(B)).toBe(true);
  });

  it('refuses to leave while an owner-bound write is in flight', async () => {
    const service = create();
    const mutation = service.beginMutation();

    await expect(service.prepareToLeave()).resolves.toEqual({ ok: false, reason: 'mutation-in-flight' });

    mutation.release();
    await expect(service.prepareToLeave()).resolves.toEqual({ ok: true });
  });

  it('refuses to leave when a draft cannot be saved under its owner', async () => {
    const service = create();
    service.onLeave(() => Promise.resolve(true));
    const unregister = service.onLeave(() => Promise.reject(new Error('quota')));

    await expect(service.prepareToLeave()).resolves.toEqual({ ok: false, reason: 'draft-save-failed' });

    unregister();
    await expect(service.prepareToLeave()).resolves.toEqual({ ok: true });
  });
});

describe('scopeResetMetaReducer', () => {
  it('drops every feature slice and keeps the scope-independent root slices', () => {
    const inner = vi.fn((state: Record<string, unknown> | undefined) => ({
      appShell: state?.['appShell'],
      httpUi: state?.['httpUi'],
      authRegistration: state?.['authRegistration'],
      customers: state?.['customers'] ?? { ids: [] },
      sales: state?.['sales'] ?? { ids: [] },
    }));
    const reducer = scopeResetMetaReducer(inner);
    const state = {
      appShell: { sidebarCollapsed: true },
      httpUi: { pendingRequests: 2 },
      authRegistration: { step: 1 },
      customers: { ids: ['customer-of-shop-a'] },
      sales: { ids: ['sale-of-shop-a'] },
    };

    const next = reducer(state, ShopContextActions.scopeChanged({ epoch: 3, reason: 'sign-out' }));

    expect(inner).toHaveBeenCalledWith(
      { appShell: state.appShell, httpUi: state.httpUi, authRegistration: state.authRegistration },
      expect.anything(),
    );
    expect(next.customers).toEqual({ ids: [] });
    expect(next.sales).toEqual({ ids: [] });
    expect(next.appShell).toBe(state.appShell);
  });

  it('passes every other action through untouched', () => {
    const inner = vi.fn((state: { value: number } | undefined) => state ?? { value: 0 });
    const reducer = scopeResetMetaReducer(inner);
    const state = { value: 7 };

    expect(reducer(state, { type: '[Other] Action' })).toBe(state);
  });
});
