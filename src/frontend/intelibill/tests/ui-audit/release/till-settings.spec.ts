import { expect, test, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { startPurchaseReceivingServer, session, SHOP_A } from './purchase-receiving-server';
import { accessibilityFixtures } from './accessibility-localization-fixtures';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const EVIDENCE = process.env['TILL_SETTINGS_EVIDENCE'];
const tr = (language: string, key: string): string =>
  key
    .split('.')
    .reduce(
      (o, part) => o[part],
      JSON.parse(
        readFileSync(resolve(__dirname, `../../../public/assets/i18n/${language}.json`), 'utf8'),
      ),
    );
const initial = () => ({
  shopId: SHOP_A,
  available: true,
  enabled: false,
  version: 0,
  openSessionCount: 0,
  awaitingReviewCount: 0,
  pendingOfflineCount: 0,
});
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let pageErrors: string[];
test.beforeEach(async ({ page }) => {
  pageErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
});
test.afterEach(async () => {
  await server?.close();
  expect(pageErrors).toEqual([]);
});
async function setup(
  page: Page,
  language: string,
  role = 'Owner',
  overrides: Partial<ReturnType<typeof initial>> = {},
) {
  page.setDefaultTimeout(10000);
  server = await startPurchaseReceivingServer(BUILD);
  const value = session();
  value.shops = value.shops.map((s) => ({ ...s, role }));
  await page.context().addInitScript(
    ({ value, language }) => {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
      localStorage.setItem('inventory.preferences.language', language);
    },
    { value, language },
  );
  await accessibilityFixtures(page);
  await page.route('**/api/expiry-policies/readiness?**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        catalogVersion: 1,
        results: {
          missingDateLots: 0,
          expiredLots: 0,
          quarantineLots: 0,
          items: [],
          totalCount: 0,
          page: 1,
          pageSize: 50,
        },
      }),
    }),
  );
  const controls = {
    state: { ...initial(), ...overrides },
    posts: [] as Record<string, unknown>[],
    drop: false,
  };
  await page.route('**/api/shops/*/till-settings', async (route) => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON();
      controls.posts.push(body);
      if (controls.drop) return route.abort('failed');
      controls.state = {
        ...controls.state,
        enabled: body.enabled,
        version: controls.state.version + 1,
      };
    }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(controls.state) });
  });
  await page.route('**/api/tills/capabilities', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        enabled: controls.state.enabled && controls.state.available,
        mixedTenderEnabled: false,
        businessDate: new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(
          new Date(),
        ),
      }),
    }),
  );
  await page.route('**/api/tills/sessions?**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
    }),
  );
  await page.goto(`${server.origin}/sales/new`);
  await page.locator('.shell-main').waitFor();
  const desktopProfile = page.locator('app-sidebar-profile-menu');
  if ((page.viewportSize()?.width ?? 1440) > 1000)
    await desktopProfile.getByRole('button').first().click();
  else {
    await page.locator('.mobile-menu-trigger').click();
    await page
      .locator('.mobile-nav-drawer')
      .getByRole('button', { name: /Receiving/ })
      .click();
  }
  await page
    .getByText(tr(language, 'shell.manageShop'), { exact: true })
    .filter({ visible: true })
    .click();
  const card = page.locator('app-shop-till-settings');
  await expect(card.locator('.settings-status')).toBeVisible();
  await card.scrollIntoViewIfNeeded();
  return { controls, card };
}
async function layoutAndCapture(page: Page, name: string) {
  const card = page.locator('app-shop-till-settings');
  await card.scrollIntoViewIfNeeded();
  await page.evaluate(() => document.fonts.ready);
  expect(await card.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1);
  const clipped = await card.locator('button,textarea,label,a').evaluateAll((nodes) =>
    nodes
      .filter((el) => {
        const box = el.getBoundingClientRect();
        return (
          box.width &&
          (box.left < -1 || box.right > innerWidth + 1 || el.scrollWidth > el.clientWidth + 2)
        );
      })
      .map((el) => el.textContent),
  );
  expect(clipped).toEqual([]);
  const audit = await new AxeBuilder({ page })
    .include('app-shop-till-settings')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .analyze();
  expect(audit.violations.map((v) => ({ id: v.id, nodes: v.nodes.map((n) => n.target) }))).toEqual(
    [],
  );
  if (EVIDENCE) {
    mkdirSync(EVIDENCE, { recursive: true });
    await card.screenshot({ path: `${EVIDENCE}/${name}.png` });
  }
}
for (const [language, width] of [
  ['en-IN', 1440],
  ['en-IN', 390],
  ['ta-IN', 390],
] as const) {
  test(`owner enables and safely disables inside Manage shop ${language} ${width}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 });
    const { card, controls } = await setup(page, language);
    await card
      .getByRole('button', { name: tr(language, 'tills.settings.enable'), exact: true })
      .click();
    const enable = card.getByRole('button', {
      name: tr(language, 'tills.settings.confirmEnable'),
      exact: true,
    });
    await expect(enable).toBeDisabled();
    await layoutAndCapture(page, `till-setup-${language}-${width}`);
    await card
      .getByLabel(tr(language, 'tills.settings.reason'), { exact: true })
      .fill('Start named cashier counts');
    await card.getByLabel(tr(language, 'tills.settings.enableConfirm'), { exact: true }).check();
    await enable.click();
    await expect(card.locator('.settings-status')).toHaveText(
      tr(language, 'tills.settings.enabled'),
    );
    expect(controls.posts[0]).toEqual({
      enabled: true,
      expectedVersion: 0,
      reason: 'Start named cashier counts',
      confirmed: true,
    });
    await layoutAndCapture(page, `till-enabled-${language}-${width}`);
    await card
      .getByRole('button', { name: tr(language, 'tills.settings.disable'), exact: true })
      .click();
    await card
      .getByLabel(tr(language, 'tills.settings.reason'), { exact: true })
      .fill('All devices reconciled');
    await card.getByLabel(tr(language, 'tills.settings.disableConfirm'), { exact: true }).check();
    await card
      .getByRole('button', { name: tr(language, 'tills.settings.confirmDisable'), exact: true })
      .click();
    await expect(card.locator('.settings-status')).toHaveText(
      tr(language, 'tills.settings.disabled'),
    );
    expect(controls.posts[1]).toEqual({
      enabled: false,
      expectedVersion: 1,
      reason: 'All devices reconciled',
      confirmed: true,
    });
  });
}
test('release unavailable, manager read-only and unresolved sessions cannot bypass setup guards', async ({
  page,
}) => {
  const { card, controls } = await setup(page, 'en-IN', 'Owner', { available: false });
  await expect(card.getByRole('button', { name: 'Set up till tracking', exact: true })).toHaveCount(
    0,
  );
  await layoutAndCapture(page, 'till-unavailable');
  controls.state = {
    ...controls.state,
    available: true,
    enabled: true,
    openSessionCount: 1,
    awaitingReviewCount: 1,
    pendingOfflineCount: 2,
  };
  await card.getByRole('button', { name: 'Refresh settings', exact: true }).click();
  await expect(
    card.getByRole('button', { name: 'Turn off till tracking', exact: true }),
  ).toBeDisabled();
  await layoutAndCapture(page, 'till-disable-blocked');
  expect(controls.posts).toHaveLength(0);
});
test('manager sees status without owner mutation controls', async ({ page }) => {
  const { card, controls } = await setup(page, 'en-IN', 'Manager');
  await expect(card).toContainText('Only the shop owner can change till tracking.');
  await expect(card.locator('[data-testid="till-settings-change"]')).toHaveCount(0);
  expect(controls.posts).toHaveLength(0);
});
test('lost response keeps the same request pending through an early status check', async ({
  page,
}) => {
  const { card, controls } = await setup(page, 'en-IN');
  controls.drop = true;
  await card.getByRole('button', { name: 'Set up till tracking', exact: true }).click();
  await card.getByLabel('Reason for this change', { exact: true }).fill('Count each drawer');
  await card.getByLabel(tr('en-IN', 'tills.settings.enableConfirm'), { exact: true }).check();
  await card.getByRole('button', { name: 'Enable till tracking', exact: true }).click();
  await expect(card).toContainText(tr('en-IN', 'tills.settings.unknown'));
  await card.getByRole('button', { name: 'Refresh settings', exact: true }).click();
  await expect(card).toContainText(tr('en-IN', 'tills.settings.unknown'));
  await expect(card.locator('[data-testid="till-settings-change"]')).toHaveCount(0);
  controls.drop = false;
  await card.getByRole('button', { name: 'Retry original change', exact: true }).click();
  await expect(card.locator('.settings-status')).toHaveText('Enabled');
  expect(controls.posts).toHaveLength(2);
  expect(controls.posts[1]).toEqual(controls.posts[0]);
});
