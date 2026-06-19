import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { authGuard } from './core/guards/auth.guard';
import { dashboardGuard } from './core/guards/dashboard.guard';
import { discountsGuard } from './core/guards/discounts.guard';
import { AuthService } from './core/auth/auth.service';
import { routes } from './app.routes';
import { shellRoutes } from './core/layout/shell.routes';
import { DashboardPageComponent } from './features/dashboard/pages/dashboard-page/dashboard-page.component';
import { CreditNotePrintPageComponent } from './features/sales/pages/credit-note-print-page/credit-note-print-page.component';
import { ServicesPageComponent } from './features/services/pages/services-page.component';

describe('app routes', () => {
  const authService = {
    isAuthenticated: vi.fn(),
    bootstrapSessionWithStatus: vi.fn(),
    canUseOfflineSalesAuthGrace: vi.fn(),
  };
  const router = {
    createUrlTree: vi.fn<Router['createUrlTree']>().mockReturnValue({ redirected: true } as never),
    parseUrl: vi.fn<Router['parseUrl']>((url: string) => ({ toString: () => url } as never)),
  };

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
    router.parseUrl.mockClear();
  });

  it('keeps invoice print outside the shell layout', () => {
    const printRouteIndex = routes.findIndex((route) => route.path === 'sales/:saleId/print');
    const shellRouteIndex = routes.findIndex((route) => route.path === '');
    const printRoute = routes[printRouteIndex];

    expect(printRoute).toBeDefined();
    expect(printRoute?.canActivate).toContain(authGuard);
    expect(printRouteIndex).toBeGreaterThanOrEqual(0);
    expect(shellRouteIndex).toBeGreaterThanOrEqual(0);
    expect(printRouteIndex).toBeLessThan(shellRouteIndex);
  });

  it('keeps credit note print outside the shell layout', () => {
    const printRouteIndex = routes.findIndex((route) => route.path === 'sales/credit-notes/:code/print');
    const shellRouteIndex = routes.findIndex((route) => route.path === '');
    const printRoute = routes[printRouteIndex];

    expect(printRoute).toBeDefined();
    expect(printRoute?.canActivate).toContain(authGuard);
    expect(printRouteIndex).toBeGreaterThanOrEqual(0);
    expect(shellRouteIndex).toBeGreaterThanOrEqual(0);
    expect(printRouteIndex).toBeLessThan(shellRouteIndex);
  });

  it('loads the credit note print component for the sales print route', async () => {
    const printRoute = routes.find((route) => route.path === 'sales/credit-notes/:code/print');

    expect(printRoute).toBeDefined();
    await expect(printRoute?.loadComponent?.()).resolves.toBe(CreditNotePrintPageComponent);
  });

  it('permits /sales/new offline grace through top-level protected route', async () => {
    const shellRoute = routes.find((route) => route.path === '');
    const shellRoot = shellRoutes.find((route) => route.path === '');
    const salesNewChildRoute = shellRoot?.children?.find((route) => route.path === 'sales/new');

    expect(shellRoute).toBeDefined();
    expect(shellRoute?.canActivate).toContain(authGuard);
    expect(shellRoute?.data?.['allowOfflineSalesGracePaths']).toEqual(['/sales/new']);
    expect(salesNewChildRoute?.canActivate).toContain(authGuard);
    expect(salesNewChildRoute?.data?.['allowOfflineSalesGrace']).toBe(true);

    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(true);

    const result = await TestBed.runInInjectionContext(async () => {
      const route = { data: shellRoute?.data ?? {} } as ActivatedRouteSnapshot;
      const state = { url: '/sales/new' } as RouterStateSnapshot;
      const guardResult = authGuard(route, state) as any;

      if (typeof guardResult === 'boolean') {
        return guardResult;
      }

      return firstValueFrom(guardResult);
    });

    expect(result).toBe(true);
  });

  it('keeps dashboard as the real shell child route outside offline grace', async () => {
    const shellRoute = routes.find((route) => route.path === '');
    const shellRoot = shellRoutes.find((route) => route.path === '');
    const dashboardRoute = shellRoot?.children?.find((route) => route.path === 'dashboard');

    expect(shellRoute).toBeDefined();
    expect(dashboardRoute).toBeDefined();
    expect(dashboardRoute?.canActivate).toContain(dashboardGuard);
    expect(dashboardRoute?.data?.['allowOfflineSalesGrace']).toBeUndefined();
    await expect(dashboardRoute?.loadComponent?.()).resolves.toBe(DashboardPageComponent);
  });

  it('lets the services route render its own permission-aware page', async () => {
    const shellRoot = shellRoutes.find((route) => route.path === '');
    const servicesRoute = shellRoot?.children?.find((route) => route.path === 'services');

    expect(servicesRoute).toBeDefined();
    expect(servicesRoute?.canActivate).toBeUndefined();
    await expect(servicesRoute?.loadComponent?.()).resolves.toBe(ServicesPageComponent);
  });

  it('blocks other protected shell URLs when API is unreachable', async () => {
    const shellRoute = routes.find((route) => route.path === '');
    const shellRoot = shellRoutes.find((route) => route.path === '');
    const discountsRoute = shellRoot?.children?.find((route) => route.path === 'discounts');

    expect(shellRoute).toBeDefined();
    expect(discountsRoute).toBeDefined();
    expect(discountsRoute?.canActivate).toContain(discountsGuard);

    authService.isAuthenticated.mockReturnValue(false);
    authService.bootstrapSessionWithStatus.mockReturnValue(of('API_UNREACHABLE'));

    const result = await TestBed.runInInjectionContext(async () => {
      const route = { data: shellRoute?.data ?? {} } as ActivatedRouteSnapshot;
      const state = { url: '/dashboard' } as RouterStateSnapshot;
      const guardResult = authGuard(route, state) as any;

      if (typeof guardResult === 'boolean') {
        return guardResult;
      }

      return firstValueFrom(guardResult);
    });

    expect(result).toEqual({ redirected: true });
    expect(authService.canUseOfflineSalesAuthGrace).not.toHaveBeenCalled();
  });
});
