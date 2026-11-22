import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';

import { installShopSetup } from './licensing-shop-setup-fixture';
import { SHOP_A, SHOP_B, startPurchaseReceivingServer } from './purchase-receiving-server';

const built = resolve(__dirname, '../../../dist/INVENTORY/browser');
const pendingStatus = 'This shop is waiting for trading access.';
const restrictedStatus = 'Trading is restricted for this shop.';
const securityStatus = 'Security suspension is enforced for this shop.';

async function openManageShop(page: Page, origin: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(origin + '/shops/setup');
  return openShellDialog(page);
}

async function openShellDialog(page: Page) {
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
    const fixture = await installShopSetup(page);
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
  const fixture = await installShopSetup(page);
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
      const fixture = await installShopSetup(page, 'Owner', 'Active');
      try {
        if (source === 'preferences') fixture.refusals.preferences = 'Licence.SecuritySuspended';
        await page.goto(server.origin + '/shops/setup/sample');
        if (source === 'acknowledge') {
          await expect(page.locator('.sample-paper')).toBeVisible();
          fixture.refusals.acknowledge = 'Licence.SecuritySuspended';
          fixture.authority[SHOP_A] = 'SecuritySuspended';
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
    const fixture = await installShopSetup(page, 'Owner', 'Active');
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
      const fixture = await installShopSetup(page, 'Owner', 'Active');
      try {
        const dialog = await openManageShop(page, server.origin);
        await expect(dialog.locator('#shop-contact-person')).toBeVisible();
        fixture.refusals[key] = 'Licence.SecuritySuspended';
        fixture.authority[SHOP_A] = 'SecuritySuspended';
        await save(dialog);
        // P04-15 withdraws the manage-shop overlay and displays the shell-level security restriction alert.
        await expect(page.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
        await expect(dialog).toHaveCount(0);
        await expect(page.locator('#shop-contact-person')).toHaveCount(0);
        await expect(page.locator('app-shop-tax-profile-form')).toHaveCount(0);
        await expect(page.locator('app-shop-till-settings')).toHaveCount(0);
      } finally {
        await server.close();
      }
    });

    test(`a trading refusal from the ${name} save allows refreshed contact recovery`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await installShopSetup(page, 'Owner', 'Active');
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

  for (const read of ['expiryRead', 'readiness', 'tillRead'] as const) {
    test(`a security refusal from the ${read} request withdraws the details already loaded`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await installShopSetup(page, 'Owner', 'Active');
      try {
        fixture.refusals[read] = 'Licence.SecuritySuspended';
        const dialog = await openManageShop(page, server.origin);
        // P04-15 withdraws the manage-shop overlay and displays the shell-level security restriction alert.
        await expect(page.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
        await expect(dialog).toHaveCount(0);
        await expect(page.locator('#shop-contact-person')).toHaveCount(0);
        await expect(page.locator('app-shop-tax-profile-form')).toHaveCount(0);
        await expect(page.locator('app-shop-till-settings')).toHaveCount(0);
        await expect(page.locator('app-expiry-policy-form')).toHaveCount(0);
        const loaded = fixture.requests.findIndex(({ path, status }) => path === `/api/shops/${SHOP_A}` && status === 200);
        const refused = fixture.requests.findIndex(({ code }) => code === 'Licence.SecuritySuspended');
        expect(loaded).toBeGreaterThanOrEqual(0);
        expect(refused).toBeGreaterThan(loaded);
      } finally {
        await server.close();
      }
    });
  }

  test('a security refusal of the preference save withdraws the checklist before its refresh answers', async ({ page }) => {
    const server = await startPurchaseReceivingServer(built);
    const fixture = await installShopSetup(page, 'Owner', 'Active');
    let release = () => {};
    try {
      fixture.setup.showOnStartup = true;
      await page.goto(server.origin + '/shops/setup');
      const startBilling = page.getByRole('button', { name: 'Start billing', exact: true });
      await expect(page.locator('.setup-steps')).toBeVisible();
      await expect(startBilling).toBeVisible();
      fixture.refusals.acknowledge = 'Licence.SecuritySuspended';
      fixture.authority[SHOP_A] = 'SecuritySuspended';
      await page.getByRole('button', { name: 'Continue later', exact: true }).click();
      await expect(page.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
      await expect(page.locator('.setup-steps')).toHaveCount(0);
      await expect(startBilling).toHaveCount(0);
      await expect(page).toHaveURL(/\/shops\/setup$/);
      const stillSuspended = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      expect((await stillSuspended).status()).toBe(403);
      await expect(page.locator('.setup-steps')).toHaveCount(0);

      // Explicit release restores the checklist, then prove an in-flight setup 200 cannot remount after suspension.
      fixture.authority[SHOP_A] = 'Active';
      const firstRelease = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      expect((await firstRelease).status()).toBe(200);
      await expect(page.locator('.setup-steps')).toBeVisible();

      fixture.setup.hold = new Promise<void>((resolve) => { release = resolve; });
      await page.getByRole('button', { name: 'Refresh setup', exact: true }).click();
      await expect.poll(() => fixture.setup.waiting).toBe(1);
      fixture.authority[SHOP_A] = 'SecuritySuspended';
      const suspendInFlight = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      expect((await suspendInFlight).status()).toBe(403);
      await expect(page.locator('.setup-steps')).toHaveCount(0);
      const staleSetup = page.waitForResponse((res) => res.url().endsWith(`/api/shops/${SHOP_A}/setup`));
      release();
      expect((await staleSetup).status()).toBe(200);
      await expect(page.locator('.setup-steps')).toHaveCount(0);

      fixture.setup.hold = undefined;
      fixture.authority[SHOP_A] = 'Active';
      const finalRelease = page.waitForResponse((res) => res.url().endsWith('/trading-authority'));
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      expect((await finalRelease).status()).toBe(200);
      await expect(page.locator('.setup-steps')).toBeVisible();
    } finally {
      release();
      await server.close();
    }
  });

  for (const phase of ['Active', 'ObserveOnly'] as const) {
    test(`a manager reads the saved tax profile in a shop that is ${phase}`, async ({ page }) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await installShopSetup(page, 'Manager', phase);
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

for (const [view, route, content, action] of [
  ['sample', '/shops/setup/sample', '.sample-paper', 'Print sample'],
  ['checklist', '/shops/setup', '.setup-steps', 'Start billing'],
] as const) {
  for (const target of ['current shop', 'other shop'] as const) {
    test(`shell refusal for the ${target} over the mounted ${view}`, async ({ page }, info) => {
      const server = await startPurchaseReceivingServer(built);
      const fixture = await installShopSetup(page, 'Owner', 'Active');
      try {
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.goto(server.origin + '/shops/setup');
        // The printable sample is a standalone route. Keep it mounted in a second tab of this session.
        const mounted = view === 'sample' ? await page.context().newPage() : page;
        const mountedFixture = mounted !== page ? await installShopSetup(mounted, 'Owner', 'Active') : fixture;
        if (mounted !== page) {
          await mounted.goto(server.origin + route);
        }
        const data = mounted.locator(content);
        const button = mounted.locator('button').filter({ hasText: action });
        await expect(data).toBeVisible();
        await expect(button).toBeEnabled();
        if (target === 'current shop') {
          fixture.refusals.tillRead = 'Licence.SecuritySuspended';
          fixture.authority[SHOP_A] = 'SecuritySuspended';
          mountedFixture.authority[SHOP_A] = 'SecuritySuspended';
        }
        const dialog = await openShellDialog(page);
        if (target === 'other shop') {
          await page.route(`**/api/shops/${SHOP_B}`, intercepted =>
            intercepted.fulfill({ status: 403, json: { code: 'Licence.SecuritySuspended' } }));
          await dialog.locator('#manage-shop-id').selectOption(SHOP_B);
          await expect(dialog.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
        } else {
          // P04-15 withdraws the manage-shop overlay and the mounted view for the restricted active shop.
          await expect(page.getByRole('alert').filter({ hasText: securityStatus }).first()).toBeVisible();
          await expect(dialog).toHaveCount(0);
        }
        const count = target === 'current shop' ? 0 : 1;
        await expect(data).toHaveCount(count);
        await expect(button).toHaveCount(count);
        if (target === 'other shop') {
          await dialog.getByRole('button', { name: 'Close' }).click();
          await expect(dialog).toHaveCount(0);
          await expect(data).toHaveCount(count);
          await expect(button).toHaveCount(count);
        }
        await mounted.screenshot({ path: info.outputPath('after-dialog-close.png') });
        if (target === 'current shop') {
          await expect(mounted.getByRole('alert')).toContainText(securityStatus);
          const suspendedCheck = mounted.waitForResponse((res) => res.url().endsWith('/trading-authority'));
          await mounted.evaluate(() => window.dispatchEvent(new Event('focus')));
          expect((await suspendedCheck).status()).toBe(403);
          await expect(data).toHaveCount(0);
          fixture.authority[SHOP_A] = 'Active';
          mountedFixture.authority[SHOP_A] = 'Active';
          const authorityRefresh = mounted.waitForResponse((res) => res.url().endsWith('/trading-authority'));
          await mounted.evaluate(() => window.dispatchEvent(new Event('focus')));
          expect((await authorityRefresh).status()).toBe(200);
        }
        await expect(data).toBeVisible();
        await expect(button).toBeEnabled();
      } finally {
        await server.close();
      }
    });
  }
}
