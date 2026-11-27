import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopPermissionsService } from '../layout/shop-permissions.service';
import { profitLossGuard } from './profit-loss.guard';

describe('profitLossGuard', () => {
  const roleSignal = signal('Owner');

  const permissionsService = {
    isOwnerOrManagerOfActiveShop: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
  } as ShopPermissionsService;

  const router = {
    createUrlTree: vi.fn(() => ({ redirected: true } as never)),
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    roleSignal.set('Owner');
    router.createUrlTree.mockClear();

    TestBed.configureTestingModule({
      providers: [
        { provide: ShopPermissionsService, useValue: permissionsService },
        { provide: Router, useValue: router },
      ],
    });
  });

  it('allows owner and manager access', () => {
    const result = TestBed.runInInjectionContext(() => profitLossGuard({} as never, {} as never));
    expect(result).toBe(true);

    roleSignal.set('Manager');
    const managerResult = TestBed.runInInjectionContext(() => profitLossGuard({} as never, {} as never));
    expect(managerResult).toBe(true);
  });

  it('redirects staff to dashboard', () => {
    roleSignal.set('Staff');

    const result = TestBed.runInInjectionContext(() => profitLossGuard({} as never, {} as never));

    expect(result).toEqual({ redirected: true });
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
  });
});
