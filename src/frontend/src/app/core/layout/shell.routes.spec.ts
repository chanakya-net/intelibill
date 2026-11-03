import { describe, expect, it } from 'vitest';

import { shellRoutes } from './shell.routes';

describe('shell routes', () => {
  it('registers purchase order list and detail routes', () => {
    const shellRoute = shellRoutes.find((route) => route.path === '');
    const childRoutes = shellRoute?.children ?? [];

    const listRoute = childRoutes.find((route) => route.path === 'inventory/purchase-orders');
    const detailRoute = childRoutes.find(
      (route) => route.path === 'inventory/purchase-orders/:purchaseOrderId',
    );

    expect(listRoute).toBeDefined();
    expect(detailRoute).toBeDefined();
    expect(listRoute?.loadComponent).toBeDefined();
    expect(detailRoute?.loadComponent).toBeDefined();
  });

  it('registers the sales credit notes route', () => {
    const shellRoute = shellRoutes.find((route) => route.path === '');
    const childRoutes = shellRoute?.children ?? [];

    const creditNotesRoute = childRoutes.find((route) => route.path === 'sales/credit-notes');

    expect(creditNotesRoute).toBeDefined();
    expect(creditNotesRoute?.loadComponent).toBeDefined();
  });

  it('registers the offline reconciliation console before the sales history route', () => {
    const shellRoute = shellRoutes.find((route) => route.path === '');
    const childRoutes = shellRoute?.children ?? [];

    const consoleIndex = childRoutes.findIndex((route) => route.path === 'sales/offline-reconciliation');
    const salesIndex = childRoutes.findIndex((route) => route.path === 'sales');

    expect(consoleIndex).toBeGreaterThanOrEqual(0);
    expect(childRoutes[consoleIndex]?.loadComponent).toBeDefined();
    expect(consoleIndex).toBeLessThan(salesIndex);
  });

  it('registers purchase order detail route before list route', () => {
    const shellRoute = shellRoutes.find((route) => route.path === '');
    const childRoutes = shellRoute?.children ?? [];

    const detailIndex = childRoutes.findIndex(
      (route) => route.path === 'inventory/purchase-orders/:purchaseOrderId',
    );
    const listIndex = childRoutes.findIndex((route) => route.path === 'inventory/purchase-orders');

    expect(detailIndex).toBeGreaterThanOrEqual(0);
    expect(listIndex).toBeGreaterThanOrEqual(0);
    expect(detailIndex).toBeLessThan(listIndex);
  });
});
