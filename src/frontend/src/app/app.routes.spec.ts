import { describe, expect, it } from 'vitest';

import { authGuard } from './core/guards/auth.guard';
import { routes } from './app.routes';

describe('app routes', () => {
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
});
