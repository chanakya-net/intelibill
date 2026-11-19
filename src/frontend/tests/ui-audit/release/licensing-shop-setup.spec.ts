import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures, SALE } from './accessibility-localization-fixtures';
import { session, shops, SHOP_A, SHOP_B, startPurchaseReceivingServer } from './purchase-receiving-server';

const built = resolve(__dirname, '../../../dist/INVENTORY/browser');
const recovery = ['name', 'contactPerson', 'mobileNumber'];
const allFields = [...recovery, 'address', 'city', 'state', 'pincode', 'gstNumber'];
const pendingStatus = 'This shop is waiting for trading access.';
const restrictedStatus = 'Trading is restricted for this shop.';
const securityStatus = 'Security suspension is enforced for this shop.';
type Phase = 'Pending' | 'Active' | 'TradingRestricted' | 'ObserveOnly';
type Refusals = Partial<Record<'acknowledge' | 'preferences' | 'expiry' | 'till', string>>;
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

async function install(page: Page, role = 'Owner', first: Phase = 'Pending') {
  await accessibilityFixtures(page);
  const phases: Record<string, Phase> = { [SHOP_A]: first, [SHOP_B]: 'Active' };
  const refusals: Refusals = {};
  const members = shops.map(shop => ({ ...shop, role }));
  const saved = { [SHOP_A]: details(SHOP_A), [SHOP_B]: details(SHOP_B) };
  const requests: RequestEvidence[] = [];
  const reply = (route: Route, json: object, status = 200, fields: string[] = [], code?: string) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, status, fields, code });
    return route.fulfill({ status, json, headers: { 'Cache-Control': 'no-store' } });
  };

  const refuse = (route: Route, code: string) => reply(route, { code, status: 403 }, 403, [], code);
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
  await page.route('**/api/shops/*/till-settings', route => route.request().method() === 'PUT' && refusals.till
    ? refuse(route, refusals.till)
    : reply(route, { shopId: SHOP_A, available: true, enabled: false, version: 0, openSessionCount: 0,
      awaitingReviewCount: 0, pendingOfflineCount: 0 }));
  await page.route('**/api/expiry-policies**', (route) => {
    if (route.request().method() === 'PUT' && refusals.expiry) return refuse(route, refusals.expiry);
    if (new URL(route.request().url()).pathname.endsWith('/readiness')) return reply(route, { catalogVersion: 1,
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
  await page.route(/\/api\/shops\/[^/]+(?:\/(?:setup|tax-profile|bank-details))?$/, (route) => {
    const [, shopId, entry] = new URL(route.request().url()).pathname.match(/\/api\/shops\/([^/]+)(?:\/(.*))?$/) ?? [];
    if (!saved[shopId]) return route.fallback();
    const hint = access(shopId, phases[shopId], role);
    if (route.request().method() === 'GET') {
      return entry === 'setup' ? reply(route, {
        shopId, contractVersion: 1, canTrade: true, setupAccess: hint,
        steps: ['business', 'tax', 'catalog', 'bank', 'sample'].map(id => ({ id, state: 'complete', reasons: [] })),
        blockingReasons: [], permissions: { canEditShop: true, canEditCatalog: hint.hasOperatingAccess,
          canSavePreferences: hint.canSavePreferences },
        preferences: { version: 1, showOnStartup: false, sampleReceiptAcknowledgedAt: null },
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
  return { phases, saved, requests, refusals, puts: () => requests.filter(entry => entry.method === 'PUT') };
}

async function openManageShop(page: Page, origin: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(origin + '/shops/setup');
  const menu = page.locator('app-sidebar-profile-menu');
  await menu.getByText(/Receiving/).click();
  await menu.getByText('Manage Shop', { exact: true }).click();
  return page.getByRole('dialog');
}

const saveExpiry = async (dialog: Locator) => {
  await dialog.locator('#expiry-reason').fill('Owner review');
  await dialog.locator('app-expiry-policy-form .save-button').click();
};

const saveTill = async (dialog: Locator) => {
  const card = dialog.locator('app-shop-till-settings');
  await card.locator('[data-testid="till-settings-change"]').click();
  await card.locator('textarea').fill('Owner review');
  await card.getByRole('checkbox').check();
  await card.getByRole('button', { name: 'Enable till tracking', exact: true }).click();
};

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

for (const [layout, viewport] of [['desktop', { width: 1440, height: 900 }], ['mobile', { width: 390, height: 844 }]] as const) {
  test(`pending setup saves profile and tax, then keeps contact recovery available on ${layout}`, async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(built);
    const fixture = await install(page);
    try {
      await page.setViewportSize(viewport);
      await page.goto(server.origin + '/shops/setup');
      await expect(page.getByText(pendingStatus)).toBeVisible();
      await expect(page.getByRole('button', { name: 'Start billing', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Add bank details' })).toHaveCount(0);
      await page.getByRole('button', { name: 'Edit tax and numbering' }).click();
      const dialog = page.getByRole('dialog');
      const tax = dialog.locator('app-shop-tax-profile-form');
      await expect(tax).toBeVisible();
      await tax.locator('input[formControlName="legalName"]').fill('Prepared legal name');
      await tax.locator('.tax-profile-save').click();
      await expect.poll(() => fixture.saved[SHOP_A].taxProfile.legalName).toBe('Prepared legal name');
      await dialog.locator('#shop-address').fill('Prepared business location');
      await dialog.getByRole('button', { name: 'Save shop profile' }).click();
      await expect(dialog.getByText('Shop profile saved.')).toBeVisible();
      await expect(dialog.getByText(pendingStatus)).toBeVisible();
      await expect(dialog.locator('app-shop-till-settings')).toHaveCount(0);
      await page.screenshot({ path: info.outputPath(`pending-${layout}.png`) });

      fixture.phases[SHOP_A] = 'TradingRestricted';
      await dialog.locator('#shop-address').fill('Uncommitted expansion');
      await dialog.getByRole('button', { name: 'Save shop profile' }).click();
      await expect(dialog.getByRole('alert')).toContainText(restrictedStatus);
      expect(fixture.saved[SHOP_A].address).toBe('Prepared business location');
      await dialog.getByRole('button', { name: 'Check again' }).click();
      await expect(dialog.getByText('You can update the shop name and contact details.')).toBeVisible();
      await expect(dialog.locator('#shop-address')).toHaveCount(0);
      await expect(tax).toHaveCount(0);
      await dialog.locator('#shop-contact-person').fill('Recovered contact');
      await dialog.getByRole('button', { name: 'Save shop profile' }).click();
      await expect.poll(() => fixture.saved[SHOP_A].contactPerson).toBe('Recovered contact');
      expect(fixture.saved[SHOP_A].address).toBe('Prepared business location');
      await page.screenshot({ path: info.outputPath(`recovery-${layout}.png`) });
      expect(fixture.puts().map(({ status }) => status)).toEqual([200, 200, 403, 200]);
      await dialog.getByRole('link', { name: 'Billing accounts', exact: true }).click();
      await expect(page).toHaveURL(/\/billing$/);
    } finally {
      mkdirSync(info.outputDir, { recursive: true });

      writeFileSync(info.outputPath('fixture-shop-setup-http.json'), JSON.stringify({
        source: 'Local API fixtures with production-built merchant assets', viewport, requests: fixture.requests,
      }, null, 2));
      await server.close();
    }
  });
}

test('manage and create use the selected target and authenticated source respectively', async ({ page }, info) => {
  const server = await startPurchaseReceivingServer(built);
  const fixture = await install(page);
  try {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(server.origin + '/shops/setup');
    const menu = page.locator('app-sidebar-profile-menu');
    await menu.getByText(/Receiving/).click();
    await menu.getByText('Manage Shop', { exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText(pendingStatus)).toBeVisible();
    await dialog.locator('#manage-shop-id').selectOption(SHOP_B);
    await expect(dialog.getByText('Active', { exact: true })).toBeVisible();
    await expect(dialog.locator('.tax-profile-save')).toBeEnabled();
    await page.screenshot({ path: info.outputPath('selected-operating-shop.png') });
    await dialog.locator('#manage-shop-id').selectOption(SHOP_A);
    await expect(dialog.getByText(pendingStatus)).toBeVisible();
    await expect(dialog.locator('app-shop-till-settings')).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Close' }).click();

    const addShop = menu.getByText('Add Shop', { exact: true });
    if (!await addShop.isVisible()) await menu.getByText(/Receiving/).click();
    await addShop.click({ timeout: 5_000 });
    await expect(dialog.getByText(pendingStatus)).toBeVisible();
    await expect(dialog.locator('form')).toHaveAttribute('inert', '');
    await expect(dialog.locator('button[type=submit]')).toBeDisabled();
    expect(fixture.requests.filter(({ method }) => method === 'POST' || method === 'PUT')).toHaveLength(0);
    await page.screenshot({ path: info.outputPath('source-shop-pending.png') });
  } finally {
    mkdirSync(info.outputDir, { recursive: true });
    writeFileSync(info.outputPath('fixture-shop-setup-http.json'), JSON.stringify({
      source: 'Local API fixtures with production-built merchant assets', requests: fixture.requests,
    }, null, 2));
    await server.close();
  }
});

test.describe('licence refusals reach what is already on screen', () => {
  for (const source of ['acknowledge', 'preferences'] as const) {
    test(`a security refusal from ${source} withdraws the open sample`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await install(page, 'Owner', 'Active');
      try {
        if (source === 'preferences') fixture.refusals.preferences = 'Licence.SecuritySuspended';
        await page.goto(server.origin + '/shops/setup/sample');
        if (source === 'acknowledge') {
          await expect(page.locator('.sample-paper')).toBeVisible();
          fixture.refusals.acknowledge = 'Licence.SecuritySuspended';
          await page.getByRole('button', { name: 'I checked this sample' }).click();
        }
        await expect(page.getByRole('alert')).toContainText(securityStatus);
        await expect(page.locator('.sample-paper')).toHaveCount(0);
        await expect(page.getByRole('button', { name: 'Print sample' })).toHaveCount(0);
      } finally {
        await server.close();
      }
    });
  }

  test('an ordinary trading refusal leaves the authorized sample readable', async ({ page }) => {
    const server = await startPurchaseReceivingServer(built);
    const fixture = await install(page, 'Owner', 'Active');
    try {
      await page.goto(server.origin + '/shops/setup/sample');
      await expect(page.locator('.sample-paper')).toBeVisible();
      fixture.refusals.acknowledge = 'Licence.TradingRestricted';
      await page.getByRole('button', { name: 'I checked this sample' }).click();
      await expect(page.getByRole('alert')).toContainText(restrictedStatus);
      await expect(page.locator('.sample-paper')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Print sample' })).toBeEnabled();
    } finally {
      await server.close();
    }
  });

  for (const [name, key, save] of [['expiry', 'expiry', saveExpiry], ['till', 'till', saveTill]] as const) {
    test(`a security refusal from the ${name} save clears the shop form`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await install(page, 'Owner', 'Active');
      try {
        const dialog = await openManageShop(page, server.origin);
        await expect(dialog.locator('#shop-contact-person')).toBeVisible();
        fixture.refusals[key] = 'Licence.SecuritySuspended';
        await save(dialog);
        await expect(dialog.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
        await expect(dialog.locator('#shop-contact-person')).toHaveCount(0);
        await expect(dialog.locator('app-shop-tax-profile-form')).toHaveCount(0);
        await expect(dialog.locator('app-shop-till-settings')).toHaveCount(0);
      } finally {
        await server.close();
      }
    });

    test(`a trading refusal from the ${name} save allows refreshed contact recovery`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await install(page, 'Owner', 'Active');
      try {
        const dialog = await openManageShop(page, server.origin);
        await expect(dialog.locator('#shop-contact-person')).toBeVisible();
        fixture.refusals[key] = 'Licence.TradingRestricted';
        fixture.phases[SHOP_A] = 'TradingRestricted';
        await save(dialog);
        await expect(dialog.locator('app-expiry-policy-form')).toHaveCount(0);
        await dialog.getByRole('button', { name: 'Check again' }).click();
        await expect(dialog.getByText('You can update the shop name and contact details.')).toBeVisible();
        await dialog.locator('#shop-contact-person').fill('Recovered contact');
        await dialog.getByRole('button', { name: 'Save shop profile' }).click();
        await expect.poll(() => fixture.saved[SHOP_A].contactPerson).toBe('Recovered contact');
        const operating = fixture.puts().filter(({ path }) => /expiry-policies|till-settings/.test(path));
        expect(operating.map(({ status }) => status)).toEqual([403]);
      } finally {
        await server.close();
      }
    });
  }

  for (const phase of ['Active', 'ObserveOnly'] as const) {
    test(`a manager reads the saved tax profile in a shop that is ${phase}`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await install(page, 'Manager', phase);
      try {
        const dialog = await openManageShop(page, server.origin);
        const tax = dialog.locator('app-shop-tax-profile-form');
        await expect(tax).toBeVisible();
        await expect(tax.locator('p-select[formControlName="taxRegime"]')).toContainText('Not registered under GST');
        const series = tax.locator('input[formControlName="invoiceSeriesCode"]');
        await expect(series).toHaveValue('A');
        await expect(series).toBeDisabled();
        const save = tax.locator('.tax-profile-save');
        await expect(save).toBeDisabled();
        await save.dispatchEvent('click');
        expect(fixture.puts().filter(({ path }) => path.endsWith('/tax-profile'))).toHaveLength(0);
      } finally {
        await server.close();
      }
    });
  }
});
