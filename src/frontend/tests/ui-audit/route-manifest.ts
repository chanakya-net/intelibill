import type { Route, Routes } from '@angular/router';

import { SUPPORTED_LANGUAGES } from '../../src/app/core/i18n/language.constants';
import { AUDIT_VIEWPORTS } from './support/layout-assertions';
import type {
  AuditRole,
  AuditViewport,
  PrintProfile,
  RouteCoverageDiff,
  RouteManifestEntry,
  RouteState,
  RouteStateProfile,
} from './route-manifest.types';

const ALL_LOCALES = [...SUPPORTED_LANGUAGES];
const ALL_ROLES: readonly AuditRole[] = ['owner', 'manager', 'staff'];
const MANAGEMENT_ROLES: readonly AuditRole[] = ['owner', 'manager'];
const ALL_VIEWPORTS: readonly AuditViewport[] = AUDIT_VIEWPORTS.map((viewport) => viewport.name);
const ROUTE_STATE_PROFILES = {
  standard: ['default', 'loading', 'empty', 'error'],
  form: ['default', 'submitting', 'validation-error', 'error'],
  print: ['default', 'loading', 'error'],
  callback: ['default', 'loading', 'error'],
  minimal: ['default'],
} as const satisfies Record<RouteStateProfile, readonly RouteState[]>;

export const ROUTE_MANIFEST: readonly RouteManifestEntry[] = [
  publicRoute('login', 'form'),
  publicRoute('forgot-password', 'form'),
  publicRoute('register', 'form'),
  publicRoute('reset-password', 'form'),
  publicRoute('auth/callback', 'callback'),
  printRoute('sales/:saleId/print', 'sale-invoice', () => ({ saleId: 'sale-001' })),
  printRoute('sales/credit-notes/:code/print', 'credit-note', () => ({ code: 'credit-note-001' })),
  printRoute('inventory/purchase-orders/:purchaseOrderId/print', 'purchase-order', () => ({
    purchaseOrderId: 'purchase-order-001',
  })),
  wildcardRoute(),
  shellRoute('dashboard', MANAGEMENT_ROLES),
  shellRoute('sales/new'),
  shellRoute('sales/profit-loss', MANAGEMENT_ROLES),
  shellRoute('sales/credit-notes'),
  shellRoute('sales'),
  shellRoute('expenses'),
  shellRoute('bank-accounts'),
  shellRoute('inventory/purchase-orders/:purchaseOrderId', ALL_ROLES, () => ({
    purchaseOrderId: 'purchase-order-001',
  })),
  shellRoute('inventory/purchase-orders'),
  shellRoute('inventory/batch'),
  shellRoute('inventory/batches'),
  shellRoute('inventory/adjustments'),
  shellRoute('inventory'),
  shellRoute('services'),
  shellRoute('suppliers'),
  shellRoute('customers'),
  shellRoute('users'),
  shellRoute('discounts', MANAGEMENT_ROLES),
];

export async function flattenRouteDestinations(routes: Routes, prefix = ''): Promise<string[]> {
  const destinations = await Promise.all(routes.map((route) => flattenRoute(route, prefix)));
  return destinations.flat();
}

export function diffRouteCoverage(
  routePaths: readonly string[],
  manifestPaths: readonly string[],
): RouteCoverageDiff {
  const routes = new Set(routePaths);
  const manifest = new Set(manifestPaths);

  return {
    missingFromManifest: [...routes].filter((path) => !manifest.has(path)).sort(),
    missingFromRoutes: [...manifest].filter((path) => !routes.has(path)).sort(),
  };
}

export function hasValidRouteStateProfile(
  entry: Pick<RouteManifestEntry, 'stateProfile' | 'states'>,
): boolean {
  const expectedStates = ROUTE_STATE_PROFILES[entry.stateProfile];

  return (
    expectedStates !== undefined &&
    expectedStates.length === entry.states.length &&
    expectedStates.every((state, index) => entry.states[index] === state)
  );
}

function publicRoute(path: string, stateProfile: RouteStateProfile): RouteManifestEntry {
  return metadata(path, 'public', 'anonymous', stateProfile, []);
}

function printRoute(
  path: string,
  document: PrintProfile['document'],
  parameterFactory: NonNullable<RouteManifestEntry['parameterFactory']>,
): RouteManifestEntry {
  return {
    ...metadata(path, 'standalone-print', 'authenticated', 'print', ALL_ROLES),
    parameterFactory,
    printProfile: { document, layout: 'print' },
    intentionalLayoutExceptions: [{ reason: 'standalone-print' }],
  };
}

function wildcardRoute(): RouteManifestEntry {
  return metadata('**', 'wildcard', 'anonymous', 'minimal', []);
}

function shellRoute(
  path: string,
  roles: readonly AuditRole[] = ALL_ROLES,
  parameterFactory?: NonNullable<RouteManifestEntry['parameterFactory']>,
): RouteManifestEntry {
  return { ...metadata(path, 'shell', 'authenticated', 'standard', roles), parameterFactory };
}

function metadata(
  path: string,
  zone: RouteManifestEntry['zone'],
  authMode: RouteManifestEntry['authMode'],
  stateProfile: RouteStateProfile,
  roles: readonly AuditRole[],
): RouteManifestEntry {
  return {
    path,
    zone,
    authMode,
    stateProfile,
    states: ROUTE_STATE_PROFILES[stateProfile],
    roles,
    featureFlags: [],
    locales: ALL_LOCALES,
    viewports: ALL_VIEWPORTS,
    intentionalLayoutExceptions: [],
  };
}

async function flattenRoute(route: Route, prefix: string): Promise<string[]> {
  if (route.redirectTo !== undefined) {
    return [];
  }

  const path = joinPath(prefix, route.path);
  const children = route.children ?? (await loadChildren(route));

  if (children !== undefined) {
    return flattenRouteDestinations(children, path);
  }

  return route.path === undefined ? [] : [path];
}

async function loadChildren(route: Route): Promise<Routes | undefined> {
  if (route.loadChildren === undefined) {
    return undefined;
  }

  const loaded = await route.loadChildren();
  return Array.isArray(loaded) ? loaded : undefined;
}

function joinPath(prefix: string, path: string | undefined): string {
  return [prefix, path].filter((segment): segment is string => Boolean(segment)).join('/');
}
