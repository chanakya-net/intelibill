import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  const authService = {
    isAuthenticated: vi.fn(),
    bootstrapSessionWithStatus: vi.fn(),
    canUseOfflineSalesAuthGrace: vi.fn(),
  };

  const urlTree = { redirected: true } as unknown;
  const router = {
    createUrlTree: vi.fn<Router['createUrlTree']>().mockReturnValue(urlTree as never),
  };

  function runGuard(routeData?: Record<string, unknown>): Promise<boolean | unknown> {
    const route = { data: routeData ?? {} } as ActivatedRouteSnapshot;
    const state = { url: '/sales/new' } as RouterStateSnapshot;

    return TestBed.runInInjectionContext(async () => {
      const result = authGuard(route, state) as any;
      if (typeof result === 'boolean') {
        return result;
      }
      if (result && typeof result.then === 'function') {
        return result;
      }

      return firstValueFrom(result);
    });
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
      ],
    });

    authService.isAuthenticated.mockReset();
    authService.bootstrapSessionWithStatus.mockReset();
    authService.canUseOfflineSalesAuthGrace.mockReset();
    router.createUrlTree.mockClear();
  });

  it('allows when user is already authenticated', async () => {
    authService.isAuthenticated.mockReturnValue(true);

    await expect(runGuard()).resolves.toBe(true);
    expect(authService.bootstrapSessionWithStatus).not.toHaveBeenCalled();
  });

  it('allows sales route when API is unreachable and offline grace is eligible', async () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(true);

    await expect(runGuard({ allowOfflineSalesGrace: true })).resolves.toBe(true);
  });

  it('blocks sales route when API is unreachable but offline grace is not eligible', async () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(false);

    await expect(runGuard({ allowOfflineSalesGrace: true })).resolves.toBe(urlTree);
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });

  it('blocks non-sales protected routes when API is unreachable', async () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));

    await expect(runGuard({})).resolves.toBe(urlTree);
    expect(authService.canUseOfflineSalesAuthGrace).not.toHaveBeenCalled();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });

  it('uses normal auth success when API is reachable', async () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('READY'));

    await expect(runGuard({ allowOfflineSalesGrace: true })).resolves.toBe(true);
    expect(authService.canUseOfflineSalesAuthGrace).not.toHaveBeenCalled();
  });
});
