import type { SupportedLanguage } from '../../src/app/core/i18n/language.constants';

export type RouteZone = 'public' | 'standalone-print' | 'shell' | 'wildcard';
export type RouteAuthMode = 'anonymous' | 'authenticated';
export type RouteState =
  | 'default'
  | 'loading'
  | 'empty'
  | 'error'
  | 'submitting'
  | 'validation-error';
export type RouteStateProfile = 'standard' | 'form' | 'print' | 'callback' | 'minimal';
export type AuditRole = 'owner' | 'manager' | 'staff';
export type AuditViewport = 'mobile' | 'desktop';
export type RouteParameterFactory = () => Readonly<Record<string, string>>;

export interface PrintProfile {
  readonly document: 'sale-invoice' | 'credit-note' | 'purchase-order';
  readonly layout: 'print';
}

export interface IntentionalLayoutException {
  readonly reason: 'standalone-print';
}

export interface RouteManifestEntry {
  readonly path: string;
  readonly zone: RouteZone;
  readonly authMode: RouteAuthMode;
  readonly parameterFactory?: RouteParameterFactory;
  readonly stateProfile: RouteStateProfile;
  readonly states: readonly RouteState[];
  readonly roles: readonly AuditRole[];
  readonly featureFlags: readonly string[];
  readonly locales: readonly SupportedLanguage[];
  readonly viewports: readonly AuditViewport[];
  readonly printProfile?: PrintProfile;
  readonly intentionalLayoutExceptions: readonly IntentionalLayoutException[];
}

export interface RouteCoverageDiff {
  readonly missingFromManifest: readonly string[];
  readonly missingFromRoutes: readonly string[];
}
