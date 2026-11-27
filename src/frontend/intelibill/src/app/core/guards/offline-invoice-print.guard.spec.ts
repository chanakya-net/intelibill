import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, convertToParamMap } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { OfflineInvoiceAccessService } from '../../features/sales/services/offline-invoice-access.service';
import { AuthService } from '../auth/auth.service';
import { offlineInvoicePrintGuard } from './offline-invoice-print.guard';

describe('offlineInvoicePrintGuard', () => {
  const authService = {
    isAuthenticated: vi.fn(),
    bootstrapSessionWithStatus: vi.fn(),
    canUseOfflineSalesAuthGrace: vi.fn(),
  };
  const access = { canRead: vi.fn() };
  const loginTree = { login: true } as unknown;
  const router = {
    createUrlTree: vi.fn<Router['createUrlTree']>().mockReturnValue(loginTree as never),
    parseUrl: vi.fn<Router['parseUrl']>((url: string) => ({ toString: () => url }) as never),
  };

  function runGuard(queryParams: Record<string, string>): Promise<unknown> {
    const route = {
      data: {},
      paramMap: convertToParamMap({ saleId: 'sale-1' }),
      queryParamMap: convertToParamMap(queryParams),
    } as unknown as ActivatedRouteSnapshot;
    const state = { url: '/sales/sale-1/print' } as RouterStateSnapshot;

    return TestBed.runInInjectionContext(async () => {
      const result = offlineInvoicePrintGuard(route, state);
      return isObservable(result) ? firstValueFrom(result) : result;
    });
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        { provide: OfflineInvoiceAccessService, useValue: access },
      ],
    });
    authService.isAuthenticated.mockReset().mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReset();
    authService.canUseOfflineSalesAuthGrace.mockReset().mockResolvedValue(true);
    access.canRead.mockReset().mockResolvedValue(true);
    router.createUrlTree.mockClear();
  });

  it('keeps ordinary server-sale printing behind normal sign-in, even when the API is unreachable', async () => {
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));

    await expect(runGuard({ template: 'a4' })).resolves.toBe(loginTree);
    expect(access.canRead).not.toHaveBeenCalled();
  });

  it('opens an offline print for a signed-in session', async () => {
    authService.isAuthenticated.mockReturnValue(true);

    await expect(runGuard({ offline: '1' })).resolves.toBe(true);
    expect(authService.bootstrapSessionWithStatus).not.toHaveBeenCalled();
  });

  it('opens an offline print once the session refreshes', async () => {
    authService.bootstrapSessionWithStatus.mockReturnValue(of('READY'));

    await expect(runGuard({ offline: '1' })).resolves.toBe(true);
    expect(access.canRead).not.toHaveBeenCalled();
  });

  it('grace_allows_owned_local_print: an unreachable API with a readable local invoice opens without login', async () => {
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));

    await expect(runGuard({ offline: '1', template: 'thermal' })).resolves.toBe(true);
    expect(access.canRead).toHaveBeenCalledWith('sale-1');
    expect(router.createUrlTree).not.toHaveBeenCalled();
  });

  it('sends an unreachable API without a readable local invoice to sign-in', async () => {
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));
    access.canRead.mockResolvedValue(false);

    await expect(runGuard({ offline: '1' })).resolves.toBe(loginTree);
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });

  it.each(['REFRESH_FAILED', 'UNAUTHENTICATED'])(
    'treats an explicit refusal (%s) as sign-in required, not as lost connectivity',
    async (status) => {
      authService.bootstrapSessionWithStatus.mockReturnValue(of(status));

      await expect(runGuard({ offline: '1' })).resolves.toBe(loginTree);
      expect(access.canRead).not.toHaveBeenCalled();
    },
  );
});
