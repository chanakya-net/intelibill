import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { discountsGuard } from './discounts.guard';

describe('discountsGuard', () => {
  const sessionSignal = signal<{
    shops: { shopId: string; shopName: string; role: string; isDefault: boolean; lastUsedAt: string | null }[];
  } | null>(null);

  const authService = {
    session: sessionSignal,
  };

  function setup(): { router: Router } {
    TestBed.configureTestingModule({
      imports: [RouterTestingModule.withRoutes([])],
      providers: [{ provide: AuthService, useValue: authService }],
    });

    return { router: TestBed.inject(Router) };
  }

  function runGuard(): ReturnType<typeof discountsGuard> {
    return TestBed.runInInjectionContext(() => discountsGuard({} as never, {} as never));
  }

  beforeEach(() => {
    sessionSignal.set({
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('allows Owner to activate the discounts route', () => {
    setup();
    sessionSignal.set({
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });

    expect(runGuard()).toBe(true);
  });

  it('allows Manager to activate the discounts route', () => {
    setup();
    sessionSignal.set({
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });

    expect(runGuard()).toBe(true);
  });

  it('redirects Staff to /dashboard (negative case)', () => {
    const { router } = setup();
    sessionSignal.set({
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });

    const result = runGuard();

    expect(result).toEqual(router.createUrlTree(['/dashboard']));
  });

  it('redirects to /dashboard when no active shop exists', () => {
    const { router } = setup();
    sessionSignal.set({ shops: [] });

    const result = runGuard();

    expect(result).toEqual(router.createUrlTree(['/dashboard']));
  });

  it('redirects to /dashboard when session is null', () => {
    const { router } = setup();
    sessionSignal.set(null);

    const result = runGuard();

    expect(result).toEqual(router.createUrlTree(['/dashboard']));
  });
});
