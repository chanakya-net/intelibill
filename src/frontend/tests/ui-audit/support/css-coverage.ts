import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import type { BrowserContext, Page, Route } from '@playwright/test';
import {
  CREDIT_NOTE_STATUSES,
  createCreditNoteScenario,
  installCreditNoteFixture,
} from '../fixtures/credit-notes.fixture';
import {
  createPurchaseOrdersScenario,
  installPurchaseOrdersFixture,
  PURCHASE_ORDER_STATUSES,
} from '../fixtures/purchase-orders.fixture';
import {
  createSaleInvoiceScenario,
  installSaleInvoiceFixture,
  NORMAL_SALE,
} from '../fixtures/sale-invoices.fixture';
import { createShellScenario } from '../fixtures/shell.fixture';
import { mockExternalRequests, waitForStablePage } from './audit-page';
import { redactArtifact } from './artifact-redaction';
import type {
  AuditRole,
  AuditViewport,
  RouteManifestEntry,
  RouteState,
  RouteZone,
} from '../route-manifest.types';

export type CoverageScenarioKind = 'core' | 'locale' | 'role' | 'flag' | 'offline' | 'print';
export type CoverageInteraction = 'submit' | null;
export interface CoverageScenario {
  readonly id: string;
  readonly kind: CoverageScenarioKind;
  readonly zone: RouteZone;
  readonly route: string;
  readonly url: string;
  readonly state: RouteState;
  readonly viewport: AuditViewport;
  readonly locale: string;
  readonly role: AuditRole | null;
  readonly featureFlags: readonly string[];
  readonly media: 'screen' | 'print';
  readonly offline: boolean;
  readonly interaction: CoverageInteraction;
}
type ScenarioInput = Omit<CoverageScenario, 'id'>;
export interface CoverageRange {
  readonly start: number;
  readonly end: number;
}
export interface CapturedStylesheet {
  readonly url: string;
  readonly text: string;
  readonly ranges: readonly CoverageRange[];
}
export interface CoverageArtifact {
  readonly schemaVersion: 1;
  readonly scenario: CoverageScenario;
  readonly browser: { readonly name: string; readonly version: string };
  readonly status: 'success' | 'failed';
  readonly error?: string;
  readonly sheets: readonly {
    readonly hash: string;
    readonly url: string;
    readonly ranges: readonly CoverageRange[];
  }[];
}

const SENSITIVE_CSS_TEXT =
  /(password|passwd|token|secret|api[_-]?key|authorization|cookie|set-cookie)\s*([:=])\s*(['"]?)([^'"\s<;,}]+)\3/gi;
const CSS_BEARER_TOKEN = /\bBearer\s+[A-Za-z0-9._~+/=-]+/gi;
const CSS_JWT = /\b[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/g;
export function scenarioId(scenario: ScenarioInput | CoverageScenario): string {
  const parts = [
    scenario.kind,
    scenario.zone,
    scenario.route,
    scenario.state,
    scenario.viewport,
    scenario.locale,
    scenario.role ?? 'anonymous',
    scenario.featureFlags.join('+') || 'no-flags',
    scenario.media,
    scenario.offline ? 'offline' : 'online',
    scenario.interaction ?? 'passive',
  ];
  return parts.join('--').replaceAll(/[^a-zA-Z0-9_-]+/g, '-').replaceAll(/^-|-$/g, '');
}
export function buildScenarioCatalog(
  manifest: readonly RouteManifestEntry[],
): readonly CoverageScenario[] {
  const scenarios = manifest.flatMap((entry) => [
    ...coreScenarios(entry),
    ...localeScenarios(entry),
    ...roleScenarios(entry),
    ...flagScenarios(entry),
    ...offlineScenarios(entry),
    ...printScenarios(entry),
  ]);
  const unique = new Map(scenarios.map((scenario) => [scenario.id, scenario]));
  return [...unique.values()].sort((left, right) => left.id.localeCompare(right.id));
}
function coreScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  return entry.states.flatMap((state) =>
    entry.viewports.map((viewport) => createScenario(entry, 'core', { state, viewport })),
  );
}
function localeScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  if (!['login', 'dashboard'].includes(entry.path)) return [];
  return entry.locales.map((locale) => createScenario(entry, 'locale', { locale }));
}

function roleScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  if (!['dashboard', 'sales'].includes(entry.path)) return [];
  return entry.roles.map((role) => createScenario(entry, 'role', { role }));
}

function flagScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  return entry.featureFlags.map((featureFlag) =>
    createScenario(entry, 'flag', { featureFlags: [featureFlag] }),
  );
}

function offlineScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  if (!['login', 'sales/new'].includes(entry.path)) return [];
  return [createScenario(entry, 'offline', { offline: true })];
}

function printScenarios(entry: RouteManifestEntry): CoverageScenario[] {
  if (entry.printProfile === undefined) return [];
  return [createScenario(entry, 'print', { media: 'print' })];
}

function createScenario(
  entry: RouteManifestEntry,
  kind: CoverageScenarioKind,
  overrides: Partial<ScenarioInput> = {},
): CoverageScenario {
  const state = overrides.state ?? 'default';
  const scenario: ScenarioInput = {
    kind,
    zone: entry.zone,
    route: entry.path,
    url: resolveRouteUrl(entry),
    state,
    viewport: overrides.viewport ?? 'desktop',
    locale: overrides.locale ?? entry.locales[0] ?? 'en-IN',
    role: overrides.role ?? entry.roles[0] ?? null,
    featureFlags: overrides.featureFlags ?? entry.featureFlags,
    media: overrides.media ?? 'screen',
    offline: overrides.offline ?? false,
    interaction:
      overrides.interaction ?? (['submitting', 'validation-error'].includes(state) ? 'submit' : null),
  };
  return { id: scenarioId(scenario), ...scenario };
}

function resolveRouteUrl(entry: RouteManifestEntry): string {
  if (entry.path === '**') return '/ui-audit-not-found';
  const parameters = entry.parameterFactory?.() ?? {};
  const path = entry.path.replaceAll(/:([^/]+)/g, (_match, name: string) => parameters[name] ?? name);
  if (path === 'reset-password') return `/${path}?token=ui-audit-token&email=audit@example.com`;
  return `/${path}`;
}

export async function writeCoverageArtifact(options: {
  readonly coverageDir: string;
  readonly scenario: CoverageScenario;
  readonly browser: CoverageArtifact['browser'];
  readonly entries?: readonly CapturedStylesheet[];
  readonly error?: unknown;
}): Promise<CoverageArtifact> {
  const stylesheetsDir = join(options.coverageDir, 'stylesheets');
  await mkdir(stylesheetsDir, { recursive: true });
  const sheets = mergeStylesheets(options.entries ?? []);
  await Promise.all(
    sheets.map(({ hash, text }) => writeStylesheet(join(stylesheetsDir, `${hash}.css`), text)),
  );
  const artifact = redactArtifact({
    schemaVersion: 1,
    scenario: options.scenario,
    browser: options.browser,
    status: options.error === undefined ? 'success' : 'failed',
    ...(options.error === undefined ? {} : { error: String(options.error) }),
    sheets: sheets.map(({ text: _text, ...sheet }) => sheet),
  }) as CoverageArtifact;
  Object.assign(artifact, { browser: options.browser });
  await writeFile(
    join(options.coverageDir, `${options.scenario.id}.json`),
    `${JSON.stringify(artifact, null, 2)}\n`,
    'utf8',
  );
  return artifact;
}

function mergeStylesheets(entries: readonly CapturedStylesheet[]) {
  const sheets = new Map<string, { hash: string; url: string; text: string; ranges: CoverageRange[] }>();
  for (const entry of entries) {
    const text = redactStylesheet(entry.text);
    const hash = createHash('sha256').update(text).digest('hex');
    const sheet = sheets.get(hash) ?? { hash, url: entry.url, text, ranges: [] };
    sheet.ranges.push(...entry.ranges);
    sheets.set(hash, sheet);
  }
  return [...sheets.values()].map((sheet) => ({
    ...sheet,
    ranges: mergeRanges(sheet.ranges),
  }));
}

function redactStylesheet(text: string): string {
  return text
    .replace(
      SENSITIVE_CSS_TEXT,
      (match: string, _key: string, _separator: string, _quote: string, value: string) =>
        match.replace(value, mask(value)),
    )
    .replace(CSS_BEARER_TOKEN, (match) => `Bearer ${mask(match.slice('Bearer '.length))}`)
    .replace(CSS_JWT, mask);
}

const mask = (value: string): string => 'x'.repeat(value.length);

function mergeRanges(ranges: readonly CoverageRange[]): CoverageRange[] {
  const sorted = [...ranges].sort((left, right) => left.start - right.start || left.end - right.end);
  const merged: CoverageRange[] = [];
  for (const range of sorted) {
    const previous = merged.at(-1);
    if (previous === undefined || range.start > previous.end) merged.push({ ...range });
    else merged[merged.length - 1] = { start: previous.start, end: Math.max(previous.end, range.end) };
  }
  return merged;
}

async function writeStylesheet(path: string, text: string): Promise<void> {
  try {
    await writeFile(path, text, { encoding: 'utf8', flag: 'wx' });
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
  }
}

export async function runCoverageScenario(options: {
  readonly page: Page;
  readonly context: BrowserContext;
  readonly scenario: CoverageScenario;
  readonly coverageDir?: string;
}): Promise<CoverageArtifact> {
  const coverageDir =
    options.coverageDir ?? join(process.cwd(), '.ui-audit', 'style-usage', 'runtime');
  const browser = {
    name: options.context.browser()?.browserType().name() ?? 'chromium',
    version: options.context.browser()?.version() ?? 'unknown',
  };
  let coverageStarted = false;
  try {
    await installCoverageFixture(options.page, options.scenario);
    await options.page.emulateMedia({ media: options.scenario.media });
    await options.page.coverage.startCSSCoverage({ resetOnNavigation: false });
    coverageStarted = true;
    await options.page.goto(options.scenario.url);
    await waitForStablePage(options.page);
    await applyCoverageState(options.page, options.scenario);
    if (options.scenario.offline) await options.context.setOffline(true);
    await options.page.waitForTimeout(100);
    const entries = await options.page.coverage.stopCSSCoverage();
    coverageStarted = false;
    return writeCoverageArtifact({ coverageDir, scenario: options.scenario, browser, entries });
  } catch (error) {
    if (coverageStarted) await options.page.coverage.stopCSSCoverage().catch(() => []);
    await writeCoverageArtifact({ coverageDir, scenario: options.scenario, browser, error });
    throw error;
  } finally {
    if (options.scenario.offline) await options.context.setOffline(false);
  }
}

async function installCoverageFixture(page: Page, scenario: CoverageScenario): Promise<void> {
  const apiState = scenario.state === 'loading' || scenario.state === 'error'
    ? scenario.state
    : 'ready';
  const fixtureOptions = {
    role: shellRole(scenario.role),
    locale: scenario.locale as 'en-IN',
    apiState,
  };
  if (scenario.route === 'sales/:saleId/print') {
    await installSaleInvoiceFixture(page, createSaleInvoiceScenario({
      ...fixtureOptions,
      sales: [{ ...NORMAL_SALE, saleId: 'sale-001' }],
    }));
    return;
  }
  if (scenario.route === 'sales/credit-notes/:code/print') {
    await installCreditNoteFixture(page, createCreditNoteScenario({
      ...fixtureOptions,
      creditNotes: [{ ...CREDIT_NOTE_STATUSES[0], code: 'credit-note-001' }],
    }));
    return;
  }
  if (scenario.route === 'inventory/purchase-orders/:purchaseOrderId/print') {
    await installPurchaseOrdersFixture(page, createPurchaseOrdersScenario({
      ...fixtureOptions,
      orders: [{ ...PURCHASE_ORDER_STATUSES[0], purchaseOrderId: 'purchase-order-001' }],
    }));
    return;
  }
  await mockExternalRequests(page);
  await seedCoverageStorage(page, scenario);
  await installStateOverride(page, scenario);
}

function shellRole(role: AuditRole | null): 'Owner' | 'Manager' | 'Staff' {
  if (role === 'manager') return 'Manager';
  if (role === 'staff') return 'Staff';
  return 'Owner';
}

async function seedCoverageStorage(page: Page, scenario: CoverageScenario): Promise<void> {
  const shell = createShellScenario({
    role: shellRole(scenario.role),
    locale: scenario.locale as 'en-IN',
    offlineEnabled: scenario.offline,
  });
  await page.addInitScript(({ session, locale, flags, authenticated }) => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem('inventory.preferences.language', locale);
    localStorage.setItem('inventory.ui-audit.feature-flags', JSON.stringify(flags));
    if (authenticated) {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(session));
    }
  }, {
    session: shell.session,
    locale: scenario.locale,
    flags: scenario.featureFlags,
    authenticated: scenario.zone === 'shell' || scenario.zone === 'standalone-print',
  });
}

async function installStateOverride(page: Page, scenario: CoverageScenario): Promise<void> {
  if (!['loading', 'empty', 'error', 'submitting'].includes(scenario.state)) return;
  await page.route('http://localhost:5277/api/**', async (route) => {
    if (isShellBootstrap(route)) {
      await route.fallback();
      return;
    }
    if (scenario.state === 'loading' || scenario.state === 'submitting') {
      if (scenario.state === 'submitting' && route.request().method() === 'GET') {
        await route.fallback();
        return;
      }
      await new Promise<void>(() => undefined);
      return;
    }
    if (scenario.state === 'error') {
      await fulfillJson(route, { title: 'UiAudit.CoverageState' }, 503);
      return;
    }
    await fulfillJson(route, { items: [], totalCount: 0, pageNumber: 1, pageSize: 20 });
  });
}

function isShellBootstrap(route: Route): boolean {
  const path = new URL(route.request().url()).pathname;
  return path === '/api/shops/me' || path.startsWith('/api/shops/') || path === '/api/ping';
}

async function applyCoverageState(page: Page, scenario: CoverageScenario): Promise<void> {
  if (!['submitting', 'validation-error', 'error'].includes(scenario.state)) return;
  if (scenario.state === 'error' && scenario.zone !== 'public') return;
  if (scenario.state !== 'validation-error') await fillVisibleForm(page);
  const submit = page.locator('button[type="submit"]:visible').first();
  if (await submit.count() && await submit.isEnabled()) {
    await submit.click();
    await page.waitForTimeout(100);
  }
}

async function fillVisibleForm(page: Page): Promise<void> {
  const inputs = page.locator('input:visible');
  for (let index = 0; index < await inputs.count(); index += 1) {
    const input = inputs.nth(index);
    const type = await input.getAttribute('type');
    if (['checkbox', 'hidden', 'radio', 'submit'].includes(type ?? '')) continue;
    const value = type === 'email' ? 'audit@example.com'
      : type === 'password' ? 'longenough1'
      : type === 'tel' ? '9800000000'
      : 'Audit';
    await input.fill(value);
  }
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
