import type { Page, Route } from '@playwright/test';

import { accessibilityFixtures, SALE } from './accessibility-localization-fixtures';
import { session, shops, SHOP_A, SHOP_B } from './purchase-receiving-server';

const recovery = ['name', 'contactPerson', 'mobileNumber'];
const allFields = [...recovery, 'address', 'city', 'state', 'pincode', 'gstNumber'];
type Phase = 'Pending' | 'Active' | 'TradingRestricted' | 'ObserveOnly';
type AuthorityPhase = 'Active' | 'SecuritySuspended';
type Refusals = Partial<Record<'acknowledge' | 'preferences' | 'expiry' | 'till' | 'expiryRead' | 'readiness' | 'tillRead', string>>;
type RequestEvidence = { method: string; path: string; status: number; fields: string[]; code?: string };

function access(shopId: string, status: Phase, role: string) {
  const owner = role === 'Owner';
  const prepare = owner && status !== 'TradingRestricted';
  const operate = status === 'Active' || status === 'ObserveOnly';
  return { shopId, status, contractVersion: 1, enforced: status !== 'ObserveOnly',
    writableProfileFields: prepare ? allFields : owner ? recovery : [],
    canPrepareTax: prepare, canSavePreferences: prepare, canEditOperatingSettings: owner && operate,
    hasOperatingAccess: operate, canCreateAdditionalShop: operate };
}

function details(shopId: string) {
  return {
    shopId, name: shopId === SHOP_A ? 'Shop A' : 'Shop B', address: '42 MG Road', city: 'Bengaluru',
    state: 'Karnataka', pincode: '560001', contactPerson: 'Owner', mobileNumber: '9999999999',
    gstNumber: null, bankName: null, bankAccountNumber: null, bankAccountType: null, ifscCode: null,
    accountHolderName: null, taxProfile: { taxRegime: 'Unregistered', legalName: 'Fixture',
      stateCode: '29', invoiceSeriesCode: 'A', eInvoiceStatus: 'NotAttested', eInvoiceAttestedAt: null },
  };
}

export async function installShopSetup(page: Page, role = 'Owner', first: Phase = 'Pending') {
  await accessibilityFixtures(page);
  const phases: Record<string, Phase> = { [SHOP_A]: first, [SHOP_B]: 'Active' };
  const authority: Record<string, AuthorityPhase> = { [SHOP_A]: 'Active', [SHOP_B]: 'Active' };
  const refusals: Refusals = {};
  /** The checklist read: what it reports, and a gate a test can close to keep a refresh unanswered. */
  const setup: { showOnStartup: boolean; hold?: Promise<void>; waiting: number } = { showOnStartup: false, waiting: 0 };
  const members = shops.map(shop => ({ ...shop, role }));
  const saved = { [SHOP_A]: details(SHOP_A), [SHOP_B]: details(SHOP_B) };
  const requests: RequestEvidence[] = [];
  const reply = (route: Route, json: object, status = 200, fields: string[] = [], code?: string) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, status, fields, code });
    return route.fulfill({ status, json, headers: { 'Cache-Control': 'no-store' } });
  };

  const refuse = (route: Route, code: string) => reply(route, { code, status: 403 }, 403, [], code);
  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    const [, shopId] = new URL(route.request().url()).pathname.match(/\/api\/licensing\/shops\/([^/]+)\/trading-authority$/) ?? [];
    if (shopId && authority[shopId] === 'SecuritySuspended') {
      return reply(route, { code: 'Licence.SecuritySuspended', enforced: true, status: 403 }, 403, [], 'Licence.SecuritySuspended');
    }
    return reply(route, {
      schemaVersion: 1, shopId: shopId ?? SHOP_A, enforced: true,
      authorityVerifiedAt: '2026-11-18T20:00:00Z', validUntil: '2026-11-20T20:00:00Z',
    });
  });
  await page.route('**/api/shops/me', route => reply(route, members));
  await page.route('**/api/shops/*/setup/sample', route => reply(route, {
    isSample: true, lineType: 'Goods', generatedAt: new Date().toISOString(), document: { ...SALE.document, number: '' },
  }));
  await page.route('**/api/shops/*/setup/preferences', (route) => {
    const code = route.request().method() === 'PUT' ? refusals.acknowledge : refusals.preferences;
    return code ? refuse(route, code) : reply(route, {
      version: 1, showOnStartup: false, sampleReceiptAcknowledgedAt: null, canSavePreferences: true,
    });
  });
  await page.route('**/api/shops/*/till-settings', (route) => {
    const code = route.request().method() === 'PUT' ? refusals.till : refusals.tillRead;
    return code ? refuse(route, code) : reply(route, { shopId: SHOP_A, available: true, enabled: false, version: 0,
      openSessionCount: 0, awaitingReviewCount: 0, pendingOfflineCount: 0 });
  });
  await page.route('**/api/expiry-policies**', (route) => {
    if (route.request().method() === 'PUT' && refusals.expiry) return refuse(route, refusals.expiry);
    const readiness = new URL(route.request().url()).pathname.endsWith('/readiness');
    const refused = readiness ? refusals.readiness : refusals.expiryRead;
    if (refused) return refuse(route, refused);
    if (readiness) return reply(route, { catalogVersion: 1,
      results: { missingDateLots: 0, expiredLots: 0, quarantineLots: 0, items: [], totalCount: 0, page: 1, pageSize: 50 } });
    const policy = { version: 1, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false };
    return reply(route, { defaultPolicy: policy, overridePolicy: null, effectivePolicy: policy, catalogVersion: 1 });
  });
  await page.route('**/api/shops', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    const code = 'Licence.PendingActivation';
    return reply(route, { code, status: 403 }, 403, Object.keys(route.request().postDataJSON()).sort(), code);
  });
  await page.route('**/api/billing/accounts', route => reply(route, { accounts: [], nextCursor: null }));
  await page.route(/\/api\/shops\/[^/]+(?:\/(?:setup|tax-profile|bank-details))?$/, async (route) => {
    const [, shopId, entry] = new URL(route.request().url()).pathname.match(/\/api\/shops\/([^/]+)(?:\/(.*))?$/) ?? [];
    if (!saved[shopId]) return route.fallback();
    const hint = access(shopId, phases[shopId], role);
    if (route.request().method() === 'GET') {
      if (entry === 'setup' && setup.hold) {
        setup.waiting++;
        await setup.hold;
      }
      return entry === 'setup' ? reply(route, {
        shopId, contractVersion: 1, canTrade: true, setupAccess: hint,
        steps: ['business', 'tax', 'catalog', 'bank', 'sample'].map(id => ({ id, state: 'complete', reasons: [] })),
        blockingReasons: [], permissions: { canEditShop: true, canEditCatalog: hint.hasOperatingAccess,
          canSavePreferences: hint.canSavePreferences },
        preferences: { version: 1, showOnStartup: setup.showOnStartup, sampleReceiptAcknowledgedAt: null },
      }) : reply(route, { ...saved[shopId], setupAccess: hint });
    }

    const payload = route.request().postDataJSON() as Record<string, unknown>;
    const fields = Object.keys(payload).sort();
    const current = saved[shopId];
    const changesProtectedProfile = ['address', 'city', 'state', 'pincode', 'gstNumber'].some(
      key => (payload[key] ?? null) !== current[key as keyof typeof current],
    );
    if ((!hint.canPrepareTax && (entry === 'tax-profile' || changesProtectedProfile)) || entry === 'bank-details') {
      const code = 'Licence.TradingRestricted';
      return reply(route, { code, status: 403 }, 403, fields, code);
    }
    saved[shopId] = entry === 'tax-profile' ? { ...current, taxProfile: { ...current.taxProfile, ...payload } }
      : { ...current, ...payload };
    return reply(route, { ...saved[shopId], setupAccess: hint }, 200, fields);
  });
  await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
    { ...session(), shops: members });
  return { phases, authority, saved, requests, refusals, setup, puts: () => requests.filter(entry => entry.method === 'PUT') };
}
