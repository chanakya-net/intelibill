import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { session, shops, SHOP_A, SHOP_B, startPurchaseReceivingServer } from './purchase-receiving-server';

const built = resolve(__dirname, '../../../dist/INVENTORY/browser');
const recovery = ['name', 'contactPerson', 'mobileNumber'];
const allFields = [...recovery, 'address', 'city', 'state', 'pincode', 'gstNumber'];
const pendingStatus = 'This shop is waiting for trading access.';
const restrictedStatus = 'Trading is restricted for this shop.';
type Phase = 'Pending' | 'Active' | 'TradingRestricted';
type RequestEvidence = { method: string; path: string; status: number; fields: string[]; code?: string };

function access(shopId: string, status: Phase) {
  const prepare = status !== 'TradingRestricted';
  const operate = status === 'Active';
  return { shopId, status, contractVersion: 1, enforced: true, writableProfileFields: prepare ? allFields : recovery,
    canPrepareTax: prepare, canSavePreferences: prepare, canEditOperatingSettings: operate,
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

async function install(page: Page) {
  await accessibilityFixtures(page);
  const phases: Record<string, Phase> = { [SHOP_A]: 'Pending', [SHOP_B]: 'Active' };
  const saved = { [SHOP_A]: details(SHOP_A), [SHOP_B]: details(SHOP_B) };
  const requests: RequestEvidence[] = [];
  const reply = (route: Route, json: object, status = 200, fields: string[] = [], code?: string) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, status, fields, code });
    return route.fulfill({ status, json, headers: { 'Cache-Control': 'no-store' } });
  };

  await page.route('**/api/shops/me', route => reply(route, shops));
  await page.route('**/api/shops', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    const code = 'Licence.PendingActivation';
    return reply(route, { code, status: 403 }, 403, Object.keys(route.request().postDataJSON()).sort(), code);
  });
  await page.route('**/api/billing/accounts', route => reply(route, { accounts: [], nextCursor: null }));
  await page.route(/\/api\/shops\/[^/]+(?:\/(?:setup|tax-profile|bank-details))?$/, (route) => {
    const [, shopId, entry] = new URL(route.request().url()).pathname.match(/\/api\/shops\/([^/]+)(?:\/(.*))?$/) ?? [];
    if (!saved[shopId]) return route.fallback();
    const hint = access(shopId, phases[shopId]);
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
  await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session());
  return { phases, saved, requests, puts: () => requests.filter(entry => entry.method === 'PUT') };
}

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
