import { readFileSync } from 'node:fs';

import type { APIRequestContext, Page } from '@playwright/test';

import type { AuthResult, AuthSession } from '../../../src/app/core/auth/auth.models';

import { redactArtifact } from './artifact-redaction';

const DEFAULT_API_BASE_URL = 'http://localhost:5277/api';
const DEFAULT_APP_BASE_URL = 'http://localhost:4200';
const REQUIRED_KEYS = ['INTELIBILL_LIVE_USERNAME', 'INTELIBILL_LIVE_PASSWORD'] as const;

type RequiredKey = (typeof REQUIRED_KEYS)[number];
type LiveEnvironment = Readonly<Record<string, string | undefined>>;
type FetchLike = (input: string | URL | Request, init?: RequestInit) => Promise<Response>;

export interface LiveConfig {
  readonly apiBaseUrl: string;
  readonly appBaseUrl: string;
  readonly username: string;
  readonly password: string;
}

export interface LiveConfigBuildResult {
  readonly config?: LiveConfig;
  readonly missing: readonly RequiredKey[];
}

export type LiveRecordType = 'item';

export interface TrackedLiveRecord {
  readonly type: LiveRecordType;
  readonly id: string;
  readonly name: string;
  readonly deleteUrl?: string;
}

export interface LiveRecordTracker {
  readonly records: readonly TrackedLiveRecord[];
  track(record: TrackedLiveRecord): void;
  assertCleanupAllowed(record: TrackedLiveRecord): void;
}

export interface LiveCleanupReport {
  readonly deleted: readonly TrackedLiveRecord[];
  readonly retained: ReadonlyArray<{
    readonly record: TrackedLiveRecord;
    readonly reason: string;
  }>;
}

export function parseEnvFile(source: string): Record<string, string> {
  const values: Record<string, string> = {};
  for (const rawLine of source.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    const assignment = line.replace(/^export\s+/, '');
    const separator = assignment.indexOf('=');
    if (separator < 1) continue;
    const key = assignment.slice(0, separator).trim();
    values[key] = unwrapEnvValue(assignment.slice(separator + 1).trim());
  }
  return values;
}

export function loadLiveEnv(environment: LiveEnvironment = process.env): LiveEnvironment {
  const envFile = environment['INTELIBILL_LIVE_ENV_FILE']?.trim();
  if (!envFile) return environment;

  try {
    return {
      ...parseEnvFile(readFileSync(envFile, 'utf8')),
      ...environment,
    };
  } catch {
    throw new Error(`Unable to load live UI audit environment file: ${envFile}`);
  }
}

export function buildLiveConfig(environment: LiveEnvironment = process.env): LiveConfigBuildResult {
  const missing = REQUIRED_KEYS.filter((key) => !environment[key]?.trim());
  if (missing.length > 0) {
    return { missing };
  }

  return {
    missing,
    config: {
      apiBaseUrl: trimTrailingSlash(environment['INTELIBILL_LIVE_API_URL'] ?? DEFAULT_API_BASE_URL),
      appBaseUrl: trimTrailingSlash(environment['INTELIBILL_LIVE_APP_URL'] ?? DEFAULT_APP_BASE_URL),
      username: environment['INTELIBILL_LIVE_USERNAME']!.trim(),
      password: environment['INTELIBILL_LIVE_PASSWORD']!,
    },
  };
}

export function requireLiveConfig(environment: LiveEnvironment = process.env): LiveConfig {
  const result = buildLiveConfig(environment);
  if (!result.config) {
    throw new Error(`Missing live UI audit configuration: ${result.missing.join(', ')}`);
  }
  assertLocalHttpUrl(result.config.apiBaseUrl, 'API');
  assertLocalHttpUrl(result.config.appBaseUrl, 'app');
  return result.config;
}

export async function pingApi(request: APIRequestContext, config: LiveConfig): Promise<void> {
  const url = `${config.apiBaseUrl}/ping`;
  let response;
  try {
    response = await request.get(url, {
      failOnStatusCode: false,
      timeout: 5_000,
    });
  } catch {
    throw new Error(
      `Live API preflight failed at ${url}. Start or configure the user-managed API.`,
    );
  }
  if (!response.ok()) {
    throw new Error(`Live API preflight failed at ${url} with HTTP ${response.status()}.`);
  }
}

export async function loginViaApi(
  request: APIRequestContext,
  config: LiveConfig,
): Promise<AuthSession> {
  const url = `${config.apiBaseUrl}/auth/login`;
  let response;
  try {
    response = await request.post(url, {
      data: { identifier: config.username, password: config.password },
      failOnStatusCode: false,
      timeout: 5_000,
    });
  } catch {
    throw new Error('Live authentication request failed before receiving a response.');
  }
  if (!response.ok()) {
    throw new Error(`Live authentication failed with HTTP ${response.status()}.`);
  }

  const result = (await response.json()) as AuthResult;
  if (!result.accessToken || !result.refreshToken || !result.user) {
    throw new Error('Live authentication returned an invalid session payload.');
  }
  return { ...result, rememberMe: true };
}

export async function seedSession(page: Page, session: AuthSession): Promise<void> {
  await page.addInitScript((authSession) => {
    sessionStorage.removeItem('inventory.auth.session.temporary');
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(authSession));
  }, session);
}

export async function installLiveApiGuard(page: Page, config: LiveConfig): Promise<void> {
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const targetUrl = rebaseApiUrl(request.url(), config.apiBaseUrl);
    assertSafeMutation(request.method(), targetUrl, config.apiBaseUrl);
    await route.continue({ url: targetUrl });
  });
}

export function redactHeaders(
  headers: Readonly<Record<string, string>>,
  secrets: readonly string[],
): Record<string, string> {
  return Object.fromEntries(
    Object.entries(headers).map(([key, value]) => [
      key,
      /authorization|cookie|token|secret/i.test(key)
        ? '[REDACTED]'
        : redactLiveValues(value, secrets),
    ]),
  );
}

export async function cleanupTrackedRecords(
  request: APIRequestContext,
  config: LiveConfig,
  accessToken: string,
  tracker: LiveRecordTracker,
): Promise<LiveCleanupReport> {
  const deleted: TrackedLiveRecord[] = [];
  const retained: Array<{ record: TrackedLiveRecord; reason: string }> = [];
  for (const record of tracker.records) {
    if (!record.deleteUrl) {
      retained.push({ record, reason: 'No supported delete API' });
      continue;
    }
    tracker.assertCleanupAllowed(record);
    assertSafeMutation('DELETE', record.deleteUrl, config.apiBaseUrl);
    try {
      const response = await request.delete(record.deleteUrl, {
        headers: { authorization: `Bearer ${accessToken}` },
        failOnStatusCode: false,
      });
      if (response.ok()) deleted.push(record);
      else retained.push({ record, reason: `Delete returned HTTP ${response.status()}` });
    } catch {
      retained.push({ record, reason: 'Delete request failed' });
    }
  }
  return { deleted, retained };
}

export function redactLiveValues<T>(value: T, secrets: readonly string[]): T {
  return redactArtifact(replaceSecretValues(value, secrets)) as T;
}

export function createRunPrefix(
  now: Date = new Date(),
  nonce: string = crypto.randomUUID().slice(0, 8),
): string {
  const timestamp = now.toISOString().replace(/\D/g, '').slice(0, 14);
  return `ui-audit-${timestamp}-${nonce}`;
}

export function createRecordTracker(runPrefix: string): LiveRecordTracker {
  const records: TrackedLiveRecord[] = [];
  return {
    records,
    track(record) {
      assertPrefixed(record, runPrefix);
      const index = records.findIndex(
        (candidate) => candidate.type === record.type && candidate.id === record.id,
      );
      if (index >= 0) records.splice(index, 1, record);
      else records.push(record);
    },
    assertCleanupAllowed(record) {
      assertPrefixed(record, runPrefix);
      const tracked = records.some(
        (candidate) =>
          candidate.type === record.type &&
          candidate.id === record.id &&
          candidate.name === record.name,
      );
      if (!tracked) {
        throw new Error('Cleanup rejected: record was not tracked by current run');
      }
    },
  };
}

export function createGuardedFetch(fetcher: FetchLike, apiBaseUrl: string): FetchLike {
  return async (input, init) => {
    const url = requestUrl(input);
    const method = init?.method ?? (input instanceof Request ? input.method : 'GET');
    assertSafeMutation(method, url, apiBaseUrl);
    return fetcher(input, init);
  };
}

export function assertSafeMutation(method: string, requestUrl: string, apiBaseUrl: string): void {
  const url = new URL(requestUrl, apiBaseUrl);
  const base = new URL(`${trimTrailingSlash(apiBaseUrl)}/`);
  const relativePath = apiRelativePath(url, base);
  const segments = relativePath.split('/').filter(Boolean);
  const unsafeToken = segments
    .flatMap((segment) => segment.toLowerCase().split(/[-_]/))
    .some((token) => ['reset', 'clear', 'truncate', 'seed', 'purge', 'sql'].includes(token));

  if (unsafeToken) {
    throw new Error('Unsafe live API request rejected');
  }
  if (method.toUpperCase() === 'DELETE' && segments.length <= 1) {
    throw new Error('Bulk DELETE request rejected');
  }
}

function apiRelativePath(url: URL, base: URL): string {
  if (url.origin !== base.origin || !url.pathname.startsWith(base.pathname)) {
    throw new Error('Live API mutation outside configured API rejected');
  }
  return url.pathname.slice(base.pathname.length);
}

function rebaseApiUrl(requestUrl: string, apiBaseUrl: string): string {
  const source = new URL(requestUrl);
  const apiMarker = '/api/';
  const markerIndex = source.pathname.indexOf(apiMarker);
  if (markerIndex < 0) {
    throw new Error('Live API request lacks expected /api/ path.');
  }
  const relativePath = source.pathname.slice(markerIndex + apiMarker.length);
  const target = new URL(`${trimTrailingSlash(apiBaseUrl)}/${relativePath}`);
  target.search = source.search;
  return target.href;
}

function requestUrl(input: string | URL | Request): string {
  if (typeof input === 'string') return input;
  return input instanceof URL ? input.href : input.url;
}

function assertPrefixed(record: TrackedLiveRecord, runPrefix: string): void {
  if (!record.name.startsWith(runPrefix)) {
    throw new Error('Cleanup rejected: record lacks current run prefix');
  }
}

function replaceSecretValues(value: unknown, secrets: readonly string[]): unknown {
  if (typeof value === 'string') {
    return secrets
      .filter(Boolean)
      .reduce((redacted, secret) => redacted.split(secret).join('[REDACTED]'), value);
  }
  if (Array.isArray(value)) {
    return value.map((item) => replaceSecretValues(item, secrets));
  }
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => [key, replaceSecretValues(item, secrets)]),
    );
  }
  return value;
}

function trimTrailingSlash(value: string): string {
  return value.trim().replace(/\/+$/, '');
}

function assertLocalHttpUrl(value: string, label: string): void {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`Live UI audit ${label} URL is invalid.`);
  }
  const localHost = ['localhost', '127.0.0.1'].includes(url.hostname);
  if (!localHost || !['http:', 'https:'].includes(url.protocol)) {
    throw new Error(`Live UI audit ${label} URL must use HTTP(S) on a local hostname.`);
  }
}

function unwrapEnvValue(value: string): string {
  if (
    value.length >= 2 &&
    ((value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'")))
  ) {
    return value.slice(1, -1);
  }
  return value;
}
