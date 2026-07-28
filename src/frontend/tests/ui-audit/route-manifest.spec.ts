import type { Routes } from '@angular/router';
import { describe, expect, it } from 'vitest';

import { SUPPORTED_LANGUAGES } from '../../src/app/core/i18n/language.constants';
import { AUDIT_VIEWPORTS } from './support/layout-assertions';
import {
  ROUTE_MANIFEST,
  diffRouteCoverage,
  flattenRouteDestinations,
  hasValidRouteStateProfile,
} from './route-manifest';

describe('UI audit route manifest', () => {
  it('catalogs every routed destination exactly once', () => {
    expect(ROUTE_MANIFEST).toHaveLength(27);
    expect(new Set(ROUTE_MANIFEST.map((entry) => entry.path))).toHaveLength(27);
    expect(ROUTE_MANIFEST.filter((entry) => entry.zone === 'public')).toHaveLength(5);
    expect(ROUTE_MANIFEST.filter((entry) => entry.zone === 'standalone-print')).toHaveLength(3);
    expect(ROUTE_MANIFEST.filter((entry) => entry.zone === 'wildcard')).toHaveLength(1);
    expect(ROUTE_MANIFEST.filter((entry) => entry.zone === 'shell')).toHaveLength(18);
  });

  it('provides deterministic factories for every route parameter', () => {
    for (const entry of ROUTE_MANIFEST) {
      const parameters = [...entry.path.matchAll(/:([^/]+)/g)].map((match) => match[1]);
      const first = entry.parameterFactory?.();
      const second = entry.parameterFactory?.();

      expect(first ?? {}).toEqual(second ?? {});
      expect(Object.keys(first ?? {}).sort()).toEqual(parameters.sort());
    }
  });

  it('validates state, role, locale and viewport metadata', () => {
    const roles = new Set(['owner', 'manager', 'staff']);
    const locales = new Set(SUPPORTED_LANGUAGES);
    const viewports = new Set(AUDIT_VIEWPORTS.map((viewport) => viewport.name));

    for (const entry of ROUTE_MANIFEST) {
      expect(hasValidRouteStateProfile(entry)).toBe(true);
      expect(entry.roles.every((role) => roles.has(role))).toBe(true);
      expect(entry.locales.length).toBeGreaterThan(0);
      expect(entry.locales.every((locale) => locales.has(locale))).toBe(true);
      expect(entry.viewports.length).toBeGreaterThan(0);
      expect(entry.viewports.every((viewport) => viewports.has(viewport))).toBe(true);
      expect(new Set(entry.featureFlags)).toHaveLength(entry.featureFlags.length);
      expect(
        new Set(entry.intentionalLayoutExceptions.map((exception) => exception.reason)),
      ).toHaveLength(entry.intentionalLayoutExceptions.length);
      expect(entry.authMode).toBe(
        entry.zone === 'public' || entry.zone === 'wildcard' ? 'anonymous' : 'authenticated',
      );
    }
  });

  it('rejects invalid route state combinations', () => {
    const malformedEntry = {
      ...ROUTE_MANIFEST[0],
      states: ['default', 'empty', 'validation-error'] as const,
    };

    expect(hasValidRouteStateProfile(malformedEntry)).toBe(false);
  });

  it('requires print metadata exactly on standalone print destinations', () => {
    for (const entry of ROUTE_MANIFEST) {
      expect(entry.printProfile !== undefined).toBe(entry.zone === 'standalone-print');
    }
  });

  it('flags uncataloged destinations as coverage drift', async () => {
    const fixture: Routes = [
      { path: 'known', component: class {} },
      { path: 'missing', component: class {} },
      { path: 'redirect', redirectTo: 'known' },
      {
        path: '',
        children: [{ path: 'nested', component: class {} }],
      },
    ];

    const destinations = await flattenRouteDestinations(fixture);
    const drift = diffRouteCoverage(destinations, ['known', 'nested']);

    expect(destinations).toEqual(['known', 'missing', 'nested']);
    expect(drift.missingFromManifest).toEqual(['missing']);
    expect(drift.missingFromRoutes).toEqual([]);
  });
});
