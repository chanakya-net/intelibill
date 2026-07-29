import type { Page, Request, Route } from '@playwright/test';

import type { AuthSession, UserShop } from '../../../src/app/core/auth/auth.models';
import type { SupportedLanguage } from '../../../src/app/core/i18n/language.constants';
import type { OfflineSalesDeviceSettings } from '../../../src/app/core/storage/offline-sales-device-settings.storage';
import type { ShopDetails } from '../../../src/app/features/shops/services/shop.service';
import type { ShopUser } from '../../../src/app/features/users/services/user-account.service';
import type { BrowserFailure } from '../support/audit-page';
import { mockExternalRequests } from '../support/audit-page';

const API_ORIGIN = 'http://localhost:5277';
const API_BASE = `${API_ORIGIN}/api`;
const AUTH_SESSION_KEY = 'inventory.auth.session.local';
const LANGUAGE_KEY = 'inventory.preferences.language';
const DEVICE_ID_PREFIX = 'intelibill.offlineSales.deviceId.v1';
const DEVICE_SETTINGS_PREFIX = 'intelibill.offlineSales.deviceSettings.v1';
const observedDeclaredErrors = new WeakMap<ShellScenario, Set<string>>();

export type ShellRole = 'Owner' | 'Manager' | 'Staff';
export type ShellApiResource = 'shops' | 'shopDetails' | 'users' | 'sales' | 'connectivity'
  | 'itemStream' | 'shellData' | 'offlineSnapshot' | 'offlineLease';
export type ShellApiState = 'ready' | 'loading';

export interface DeclaredShellError {
  readonly method: string;
  readonly url: string;
  readonly status: number;
  readonly body?: unknown;
}

export interface ShellScenario {
  readonly role: ShellRole;
  readonly locale: SupportedLanguage;
  readonly session: AuthSession;
  readonly shops: readonly UserShop[];
  readonly shopDetails: Readonly<Record<string, ShopDetails>>;
  readonly users: readonly ShopUser[];
  readonly offlineSettings: Readonly<Record<string, OfflineSalesDeviceSettings>>;
  readonly apiStates: Readonly<Partial<Record<ShellApiResource, ShellApiState>>>;
  readonly declaredErrors: readonly DeclaredShellError[];
}

export interface ShellScenarioOptions {
  readonly role?: ShellRole;
  readonly locale?: SupportedLanguage;
  readonly offlineEnabled?: boolean;
  readonly longLabels?: boolean;
  readonly apiStates?: ShellScenario['apiStates'];
  readonly declaredErrors?: readonly DeclaredShellError[];
}

export interface ShellBrowserFailure extends BrowserFailure {
  readonly method?: string;
}

export const SHELL_ENGLISH_VIEWPORTS = [
  { name: 'mobile', width: 360, height: 800 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'landscape', width: 1024, height: 768 },
  { name: 'desktop', width: 1440, height: 900 },
] as const;

export const SHELL_LOCALE_VIEWPORTS = [SHELL_ENGLISH_VIEWPORTS[0], SHELL_ENGLISH_VIEWPORTS[3]] as const;

export function createShellScenario(options: ShellScenarioOptions = {}): ShellScenario {
  const role = options.role ?? 'Owner';
  const locale = options.locale ?? 'en-IN';
  const shops = createShops(role, options.longLabels ?? false);
  const user = {
    id: `user-${role.toLowerCase()}`,
    email: `${role.toLowerCase()}@ui-audit.example`,
    phoneNumber: '+919999999999',
    firstName: options.longLabels ? 'Alexandria-Cassandra-Long-Profile-Name' : role,
    lastName: 'Auditor',
    language: locale,
  };
  const session: AuthSession = {
    accessToken: `ui-audit-${role.toLowerCase()}-access`,
    refreshToken: `ui-audit-${role.toLowerCase()}-refresh`,
    accessTokenExpiresAt: '2099-01-01T00:00:00.000Z',
    refreshTokenExpiresAt: '2099-02-01T00:00:00.000Z',
    rememberMe: true,
    user,
    activeShopId: shops[0].shopId,
    shops,
  };
  const shopDetails = Object.fromEntries(shops.map((shop, index) => [
    shop.shopId,
    createShopDetails(shop, index, user.id),
  ]));
  const users: readonly ShopUser[] = [
    {
      userId: user.id,
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      phoneNumber: user.phoneNumber,
      role,
      isLoginEnabled: true,
      shopIds: shops.map((shop) => shop.shopId),
    },
  ];
  const offlineSettings = Object.fromEntries(shops.map((shop) => [
    shop.shopId,
    createOfflineSettings(shop.shopId, user.id, options.offlineEnabled ?? false),
  ]));

  return {
    role, locale, session, shops, shopDetails, users, offlineSettings,
    apiStates: options.apiStates ?? {}, declaredErrors: options.declaredErrors ?? [],
  };
}

export async function installShellFixture(page: Page, scenario: ShellScenario): Promise<void> {
  await page.evaluate(() => {
    (window as any).print = () => undefined;
  });
  await mockExternalRequests(page);
  await seedShellStorage(page, scenario);
  await page.route(`${API_BASE}/**`, async (route) => {
    await handleApiRoute(route, scenario);
  });
}

export function filterDeclaredShellFailures(
  failures: readonly ShellBrowserFailure[],
  declaration: ShellScenario | readonly DeclaredShellError[],
): BrowserFailure[] {
  const declaredErrors = Array.isArray(declaration) ? declaration : declaration.declaredErrors;
  const observedErrors = Array.isArray(declaration)
    ? new Set<string>() : observedDeclaredErrors.get(declaration) ?? new Set<string>();
  const loadingUrls = Array.isArray(declaration) ? new Set<string>() : loadingRequestUrls(declaration);

  return failures.filter((failure) => {
    if (
      failure.kind === 'request' &&
      failure.message === 'net::ERR_ABORTED' &&
      failure.url &&
      loadingUrls.has(failure.url)
    ) {
      return false;
    }
    if (
      failure.kind === 'console' &&
      [...observedErrors].some((key) =>
        failure.message.includes(`status of ${key.split('|').at(-1)} `),
      )
    ) {
      return false;
    }
    if (failure.kind !== 'response' || !failure.url) {
      return true;
    }

    const status = Number.parseInt(failure.message.replace('HTTP ', ''), 10);
    return !declaredErrors.some(
      (declared) =>
        declared.url === failure.url &&
        declared.status === status &&
        (!failure.method || declared.method === failure.method),
    );
  });
}

function loadingRequestUrls(scenario: ShellScenario): ReadonlySet<string> {
  const urls = new Set<string>();
  if (scenario.apiStates.shopDetails === 'loading') {
    for (const shop of scenario.shops) {
      urls.add(`${API_BASE}/shops/${shop.shopId}`);
    }
  }
  if (scenario.apiStates.offlineSnapshot === 'loading') {
    urls.add(`${API_BASE}/sales/offline-snapshot/stream?ngsw-bypass=true`);
  }
  return urls;
}

function createShops(role: ShellRole, longLabels: boolean): readonly UserShop[] {
  const prefix = longLabels
    ? 'Very Long International Wholesale and Retail Shop Name'
    : 'Audit Shop';

  return [
    { shopId: 'shop-primary', shopName: `${prefix} Primary`, role, isDefault: true,
      lastUsedAt: '2026-07-29T00:00:00.000Z' },
    { shopId: 'shop-secondary', shopName: `${prefix} Secondary`, role, isDefault: false,
      lastUsedAt: '2026-07-28T00:00:00.000Z' },
  ];
}

function createShopDetails(shop: UserShop, index: number, userId: string): ShopDetails {
  return {
    shopId: shop.shopId,
    name: shop.shopName,
    address: '123 Deterministic Audit Avenue',
    city: 'Bengaluru',
    state: 'Karnataka',
    pincode: index === 0 ? '560001' : '560002',
    contactPerson: 'UI Audit',
    mobileNumber: '+919999999999',
    gstNumber: '29ABCDE1234F1Z5',
    bankName: 'Audit Bank',
    bankAccountNumber: '1234567890',
    bankAccountType: 'Current',
    ifscCode: 'AUDT0000001',
    accountHolderName: shop.shopName,
    logoUrl: null,
    members: [
      {
        userId,
        firstName: 'UI',
        lastName: 'Audit',
        email: 'audit@example.test',
        phoneNumber: '+919999999999',
        role: shop.role,
      },
    ],
  };
}

function createOfflineSettings(shopId: string, userId: string, enabled: boolean): OfflineSalesDeviceSettings {
  return {
    shopId,
    deviceId: `device-${shopId}`,
    label: `Audit device for ${shopId}`,
    enabled,
    enabledAt: enabled ? '2026-07-29T00:00:00.000Z' : null,
    enabledByUserId: enabled ? userId : null,
    enabledByUserName: enabled ? 'UI Audit' : null,
    lastCompleteSnapshotAt: enabled ? '2026-07-29T00:00:00.000Z' : null,
    lastApiVerifiedAt: enabled ? '2026-07-29T00:00:00.000Z' : null,
    lastSnapshotWarningMarker: null,
    lastReservedLease: enabled
      ? { leaseId: 'lease-ui-audit', fiscalYear: '2026-2027', remainingCount: 100,
          expiresAt: '2099-01-01T00:00:00.000Z' }
      : null,
  };
}

async function seedShellStorage(page: Page, scenario: ShellScenario): Promise<void> {
  await page.addInitScript(
    ({ session, locale, settings, authKey, languageKey, devicePrefix, settingsPrefix }) => {
      localStorage.clear();
      sessionStorage.clear();
      localStorage.setItem(authKey, JSON.stringify(session));
      localStorage.setItem(languageKey, locale);
      for (const value of Object.values(settings)) {
        localStorage.setItem(`${devicePrefix}:${value.shopId}`, value.deviceId);
        localStorage.setItem(`${settingsPrefix}:${value.shopId}:${value.deviceId}`, JSON.stringify(value));
      }
    },
    {
      session: scenario.session,
      locale: scenario.locale,
      settings: scenario.offlineSettings,
      authKey: AUTH_SESSION_KEY,
      languageKey: LANGUAGE_KEY,
      devicePrefix: DEVICE_ID_PREFIX,
      settingsPrefix: DEVICE_SETTINGS_PREFIX,
    },
  );
}

async function handleApiRoute(route: Route, scenario: ShellScenario): Promise<void> {
  const request = route.request();
  const declaredError = scenario.declaredErrors.find((error) => matchesError(request, error));
  if (declaredError) {
    const observed = observedDeclaredErrors.get(scenario) ?? new Set<string>();
    observed.add(`${declaredError.method}|${declaredError.url}|${declaredError.status}`);
    observedDeclaredErrors.set(scenario, observed);
    await fulfillJson(route, declaredError.body ?? { title: 'UiAudit.DeclaredError' }, declaredError.status);
    return;
  }

  const resource = resourceForRequest(request);
  if (!resource) {
    await route.abort('blockedbyclient');
    return;
  }
  if (scenario.apiStates[resource] === 'loading') {
    await new Promise<void>(() => undefined);
    return;
  }

  await fulfillKnownRequest(route, request, scenario);
}

function matchesError(request: Request, error: DeclaredShellError): boolean {
  return request.method() === error.method && request.url() === error.url;
}

function resourceForRequest(request: Request): ShellApiResource | null {
  const path = new URL(request.url()).pathname;
  if (request.method() === 'GET' && path === '/api/shops/me') return 'shops';
  if (request.method() === 'GET' && /^\/api\/shops\/[^/]+$/.test(path)) return 'shopDetails';
  if (request.method() === 'GET' && path === '/api/users') return 'users';
  if (request.method() === 'GET' && path === '/api/sales') return 'sales';
  if (request.method() === 'GET' && path === '/api/ping') return 'connectivity';
  if (request.method() === 'GET' && path === '/api/items/stream') return 'itemStream';
  if (request.method() === 'GET' && ['/api/suppliers', '/api/bank-accounts', '/api/items'].includes(path)) {
    return 'shellData';
  }
  if (request.method() === 'GET' && path === '/api/sales/offline-snapshot/stream') {
    return 'offlineSnapshot';
  }
  if (request.method() === 'POST' && path === '/api/sales/invoice-leases/reserve') {
    return 'offlineLease';
  }
  if (knownMutation(request)) return 'shops';
  return null;
}

function knownMutation(request: Request): boolean {
  const path = new URL(request.url()).pathname;
  const method = request.method();
  return (
    (method === 'POST' && ['/api/shops', '/api/shops/default'].includes(path)) ||
    (method === 'PUT' && path === '/api/users/me') ||
    (method === 'POST' && path === '/api/users/me/change-password') ||
    (method === 'POST' && path === '/api/sales/invoice-leases/reserve') ||
    (method === 'PUT' && /^\/api\/shops\/[^/]+(?:\/bank-details)?$/.test(path))
  );
}

async function fulfillKnownRequest(route: Route, request: Request, scenario: ShellScenario): Promise<void> {
  const path = new URL(request.url()).pathname;
  if (request.method() === 'GET' && path === '/api/shops/me') {
    await fulfillJson(route, scenario.shops);
  } else if (request.method() === 'GET' && path === '/api/users') {
    await fulfillJson(route, scenario.users);
  } else if (request.method() === 'GET' && path === '/api/sales') {
    await fulfillJson(route, {
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      summary: { periodSales: 0, invoiceCount: 0, refundAmount: 0 },
    });
  } else if (request.method() === 'GET' && path === '/api/ping') {
    await fulfillJson(route, { serverTime: '2026-07-29T00:00:00.000Z' });
  } else if (request.method() === 'GET' && path === '/api/items/stream') {
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body: '' });
  } else if (request.method() === 'GET' && ['/api/suppliers', '/api/bank-accounts'].includes(path)) {
    await fulfillJson(route, []);
  } else if (request.method() === 'GET' && path === '/api/items') {
    await fulfillJson(route, { items: [], totalCount: 0, pageNumber: 1, pageSize: 20 });
  } else if (request.method() === 'GET' && path === '/api/sales/offline-snapshot/stream') {
    await route.fulfill({ status: 200, contentType: 'application/x-ndjson', body: '' });
  } else if (request.method() === 'POST' && path === '/api/sales/invoice-leases/reserve') {
    await fulfillJson(route, {
      leaseId: 'lease-ui-audit',
      shopId: scenario.session.activeShopId,
      deviceId: `device-${scenario.session.activeShopId}`,
      fiscalYear: '2026-2027',
      prefix: 'INV',
      numberPadding: 6,
      rangeStart: 1,
      rangeEnd: 100,
      nextNumber: 1,
      remainingCount: 100,
      reservedAt: '2026-07-29T00:00:00.000Z',
      expiresAt: '2099-01-01T00:00:00.000Z',
    });
  } else if (request.method() === 'GET' && path.startsWith('/api/shops/')) {
    const shopId = path.split('/').at(-1) ?? '';
    await fulfillJson(route, scenario.shopDetails[shopId]);
  } else if (request.method() === 'POST' && path === '/api/users/me/change-password') {
    await route.fulfill({ status: 204 });
  } else if (request.method() === 'PUT' && path.startsWith('/api/shops/')) {
    const segments = path.split('/');
    await fulfillJson(route, scenario.shopDetails[segments[3]]);
  } else {
    await fulfillJson(route, scenario.session);
  }
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}
