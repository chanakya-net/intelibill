import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Locator } from '@playwright/test';
import { resolve } from 'node:path';
import { readFileSync, mkdirSync } from 'node:fs';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import { startPurchaseReceivingServer, session, PO } from './purchase-receiving-server';
import { accessibilityFixtures, LONG_NAME, SALE } from './accessibility-localization-fixtures';
const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const EVIDENCE = resolve(
  __dirname,
  '../../../../../docs/reviews/2026-11-09-accessible-localized-workflows-evidence',
);
const tr = (lang: string, key: string): string =>
  key
    .split('.')
    .reduce(
      (o, k) => o[k],
      JSON.parse(
        readFileSync(resolve(__dirname, `../../../public/assets/i18n/${lang}.json`), 'utf8'),
      ),
    );
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let controls: Awaited<ReturnType<typeof accessibilityFixtures>>;
test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  await page.context().addInitScript((value) => {
    localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
    window.print = () => {};
  }, session());
  controls = await accessibilityFixtures(page);
});
test.afterEach(async () => server.close());
test.beforeEach(async ({ page }) => page.setDefaultTimeout(10000));
async function settle(page: Page) {
  await page.evaluate(async () => {
    await document.fonts.ready;
    await Promise.all(
      document
        .getAnimations()
        .filter((a) => a.effect?.getTiming().iterations !== Infinity)
        .map((a) => a.finished.catch(() => {})),
    );
  });
}
async function scan(page: Page, selector: string) {
  await settle(page);
  const result = await new AxeBuilder({ page })
    .include(selector)
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map((v) => ({
      id: v.id,
      impact: v.impact,
      nodes: v.nodes.map((n) => ({ target: n.target, html: n.html, summary: n.failureSummary })),
    })),
    `axe ${selector}`,
  ).toEqual([]);
}
async function layout(page: Page, root: Locator) {
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth - innerWidth),
    JSON.stringify(
      await page.locator('body *').evaluateAll((nodes) =>
        nodes
          .filter(
            (e) =>
              e.getBoundingClientRect().right > innerWidth + 1 && e.getBoundingClientRect().width,
          )
          .map((e) => ({
            tag: e.tagName,
            cls: e.className,
            text: e.textContent?.slice(0, 100),
            w: e.getBoundingClientRect().width,
            right: e.getBoundingClientRect().right,
          }))
          .slice(0, 20),
      ),
    ),
  ).toBeLessThanOrEqual(1);
  for (const region of await root
    .locator(
      '.statement-table-wrap, .cases-table-wrap, .p-datatable-table-container, .invoice__items-scroll',
    )
    .all()) {
    if (await region.evaluate((el) => el.scrollWidth > el.clientWidth + 1)) {
      await expect(region).toHaveAttribute('role', 'region');
      await expect(region).toHaveAccessibleName(/.+/);
      await region.focus();
      await page.keyboard.press('ArrowRight');
      await expect.poll(() => region.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0);
    }
  }
  const clipped = await root.locator('button,[role="alert"],.field-error').evaluateAll((nodes) =>
    nodes
      .filter((el) => {
        const r = el.getBoundingClientRect();
        if (
          !r.width ||
          !r.height ||
          getComputedStyle(el).visibility === 'hidden' ||
          el.closest('table')
        )
          return false;
        return r.left < -1 || r.right > innerWidth + 1 || el.scrollWidth > el.clientWidth + 2;
      })
      .map((el) => ({ text: el.textContent?.trim(), class: el.className })),
  );
  expect(clipped, 'clipped action/error boxes').toEqual([]);
}
async function capture(page: Page, lang: string, mode: string, view: string) {
  if (lang !== 'ta-IN' || mode === '200pct-reflow') return;
  mkdirSync(EVIDENCE, { recursive: true });
  if (view === 'pos') {
    const input = page.locator('app-batch-search-bar input').first();
    await input.focus();
    await input.press('Escape');
  }
  await page
    .locator('.p-datatable-table-container')
    .evaluateAll((nodes) => nodes.forEach((el) => (el.scrollLeft = 0)));
  await page.evaluate(() => window.scrollTo(0, 0));
  await settle(page);
  await page.screenshot({
    path: `${EVIDENCE}/${lang}-${mode}-${view}.png`,
    fullPage: view === 'pos' || view === 'offline-review',
  });
}
async function activate(l: Locator) {
  await expect(l).toBeEnabled();
  await l.focus();
  await l.press('Enter');
}
async function pos(page: Page, lang = 'en-IN') {
  await page.goto(`${server.origin}/sales/new`);
  await expect(page.locator('html')).toHaveAttribute('lang', lang);
  const input = page.locator('app-batch-search-bar input').first();
  await expect(input).toHaveAccessibleName(/.+/);
  await input.fill('NO-MATCH');
  await expect(page.locator('.p-autocomplete-empty-message')).toHaveText(
    tr(lang, 'common.noResults'),
  );
  await input.press('Escape');
  await input.fill('A11Y');
  await input.press('Enter');
  const picker = page.getByRole('dialog', {
    name: tr(lang, 'sales.newSale.selectBatch'),
    exact: true,
  });
  await expect(picker).toBeVisible();
  await expect(picker.locator('.batch-option').first()).toBeFocused();
  await expect(picker.locator('.p-dialog-header button')).toHaveAccessibleName(
    tr(lang, 'common.close'),
  );
  await scan(page, '.p-dialog');
  await picker.locator('.batch-option').first().press('Enter');
  const quantity = picker.locator('app-quantity-field input');
  await quantity.fill('2');
  await quantity.press('Tab');
  await activate(
    picker.getByRole('button', { name: new RegExp(tr(lang, 'sales.newSale.addToCart') + '$') }),
  );
  await expect(picker).toBeHidden();
  await expect(input).toBeFocused();
  const committedQuantity = page.locator('app-cart-table app-quantity-field input');
  await committedQuantity.fill('2');
  await committedQuantity.press('Tab');
  await expect(committedQuantity).toHaveValue('2');
  await expect(page.locator('app-cart-table app-quantity-field input')).toHaveAccessibleName(
    /DUPLICATE-LOT/,
  );
  await expect(page.locator('app-cart-table')).toContainText(
    await page.evaluate(
      (lang) =>
        new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeZone: 'UTC' }).format(
          new Date(Date.UTC(2099, 11, 31)),
        ),
      lang,
    ),
  );
  const customer = page.locator('app-sale-customer-section p-autocomplete input');
  await customer.fill(LONG_NAME.slice(0, 12));
  await expect(page.getByRole('option').filter({ hasText: LONG_NAME }).first()).toBeVisible();
  await customer.press('ArrowDown');
  await customer.press('Enter');
  await expect(customer).toHaveValue(LONG_NAME);
  await expect(page.locator('app-cart-checkout-summary button').last()).toBeEnabled();
}
async function account(page: Page, lang = 'en-IN') {
  await page.goto(`${server.origin}/customers`);
  const opener = page
    .locator('.customer-card__identity:visible,.customer-name-button:visible')
    .first();
  const actual = (await opener.count())
    ? opener
    : page.getByRole('button').filter({ hasText: LONG_NAME }).first();
  await activate(actual);
  const d = page.getByRole('dialog', { name: tr(lang, 'customers.account.title'), exact: true });
  await expect(d).toBeVisible();
  await expect(d.locator('[formcontrolname="amount"] input')).toBeVisible();
  return { d, opener: actual };
}
async function receiving(page: Page, lang = 'en-IN') {
  await page.goto(`${server.origin}/inventory/purchase-orders/${PO}`);
  const opener = page
    .locator('.po-detail-actions button')
    .filter({ hasText: tr(lang, 'purchaseOrders.actions.receive') })
    .first();
  await activate(opener);
  const d = page.getByRole('dialog').filter({ has: page.locator('[data-testid="receipt-row"]') });
  await expect(d).toBeVisible();
  return { d, opener };
}
for (const lang of SUPPORTED_LANGUAGES)
  test(`nine_locales_fit_core_journeys ${lang}`, async ({ page, browser }, info) => {
    test.setTimeout(240000);
    await page.addInitScript(
      (lang) => localStorage.setItem('inventory.preferences.language', lang),
      lang,
    );
    for (const [mode, width] of [
      ['desktop', 1280],
      ['mobile', 320],
      ['200pct-reflow', 640],
    ] as const) {
      // Equivalent CSS viewport for 1280px at 200% browser zoom; not a manual browser zoom test.
      await page.setViewportSize({ width, height: 900 });
      await pos(page, lang);
      expect(controls.salePosts).toHaveLength(0);
      await expect(
        page.getByRole('heading', { name: tr(lang, 'sales.newSale.title'), exact: true }),
      ).toBeVisible();
      await scan(page, '.new-sale-page');
      await layout(page, page.locator('.new-sale-page'));
      await capture(page, lang, mode, 'pos');
      await activate(page.locator('.new-sale-cart p-button button').first());
      const { d } = await account(page, lang);
      await scan(page, '.account-statement-dialog');
      await layout(page, d);
      await capture(page, lang, mode, 'account');
      await page.keyboard.press('Escape');
      await expect(d).toBeHidden();
      const receipt = await receiving(page, lang);
      await scan(page, '.p-dialog');
      await layout(page, receipt.d);
      await capture(page, lang, mode, 'receiving');
      await page.keyboard.press('Escape');
      await expect(receipt.d).toBeHidden();
      await page.goto(`${server.origin}/sales/offline-reconciliation`);
      await expect(
        page.getByRole('heading', {
          name: tr(lang, 'sales.offlineReconciliation.title'),
          exact: true,
        }),
      ).toBeVisible();
      await expect(page.locator('.case-open-btn')).toContainText('A/2627/000124');
      await scan(page, '.reconciliation-page');
      await layout(page, page.locator('.reconciliation-page'));
      await capture(page, lang, mode, 'offline-review');
      for (const template of ['a4', 'thermal']) {
        await page.goto(`${server.origin}/sales/${SALE.saleId}/print?template=${template}`);
        await expect(page.locator('html')).toHaveAttribute('lang', lang);
        await expect(page.locator('.invoice-document')).toContainText('Frozen Market');
        await expect(
          page.getByRole('button', { name: tr(lang, 'sales.invoice.printAgain'), exact: true }),
        ).toBeVisible();
        await expect(page.locator('.invoice-document')).toContainText('A/2627/000123');
        // Frozen canonical fixture facts remain exact while their date display localizes.
        await expect(page.locator('.invoice-document time[datetime="2026-11-09"]')).toBeVisible();
        if (template === 'a4' && width === 320)
          expect(
            await page
              .locator('.invoice__shop-name')
              .evaluate((el) => el.getBoundingClientRect().height),
            'seller identity readable without one-character wrapping',
          ).toBeLessThan(120);
        await scan(page, '.invoice-print-page');
        await layout(page, page.locator('.invoice-print-page'));
        if (lang === 'ta-IN' && mode === 'mobile') {
          mkdirSync(EVIDENCE, { recursive: true });
          await page.screenshot({
            path: `${EVIDENCE}/${lang}-${mode}-${template}.png`,
            fullPage: true,
          });
        }
      }
    }
    info.annotations.push({ type: 'browser', description: browser.version() });
  });
test('keyboard_checkout_returns_focus and same-key unknown recovery', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await pos(page);
  expect(controls.salePosts).toHaveLength(0);
  const paid = page.locator('app-sale-payment-section p-inputnumber').first().locator('input');
  await paid.fill('50');
  await paid.press('Tab');
  await expect(page.getByLabel('Due Amount', { exact: true })).toHaveValue('50');
  controls.saleMode = 'hold';
  await activate(page.locator('app-cart-checkout-summary button').last());
  await expect.poll(() => controls.salePosts.length).toBe(1);
  await page.keyboard.press('Enter');
  expect(controls.salePosts).toHaveLength(1);
  await controls.held.shift()!();
  const success = page.getByRole('dialog', { name: 'Sale Recorded', exact: true });
  await expect(success).toBeVisible();
  await expect(success.locator('[data-dialog-initial-focus]')).toBeFocused();
  await scan(page, '.p-dialog');
  const popup = page.waitForEvent('popup');
  await activate(success.getByRole('button', { name: /Print A4$/ }));
  const print = await popup;
  await print.waitForLoadState();
  await expect(print.locator('.invoice-document')).toContainText('Frozen Market');
  await print.close();
  await activate(success.getByRole('button', { name: /Done$/ }));
  await expect(success).toBeHidden();
  await expect(page.locator('main h1').first()).toBeFocused();
  await pos(page);
  controls.saleMode = 'drop';
  await activate(page.locator('app-cart-checkout-summary button').last());
  await expect.poll(() => controls.salePosts.length).toBe(2);
  await expect(page.locator('.pending-checkouts')).toBeVisible();
  const original = controls.salePosts[1];
  await page.reload();
  await activate(page.getByRole('button', { name: 'Retry same checkout', exact: true }));
  await expect.poll(() => controls.salePosts.length).toBe(3);
  expect(controls.salePosts[2]).toEqual(original);
});
test('account guarded Escape, field association and unknown reopen', async ({ page }) => {
  const { d, opener } = await account(page);
  const amount = d.locator('[formcontrolname="amount"] input');
  await expect(
    d.locator('[data-dialog-initial-focus]'),
    await page.evaluate(() => document.activeElement?.outerHTML),
  ).toBeFocused();
  await expect(amount).toHaveAccessibleName(/Amount/);
  await amount.fill('5');
  await amount.press('Tab');
  controls.paymentMode = 'hold';
  await activate(d.getByRole('button', { name: /Submit Payment$/ }));
  await expect.poll(() => controls.paymentPosts.length).toBe(1);
  await page.keyboard.press('Escape');
  await expect(d).toBeVisible();
  await controls.held.shift()!();
  await expect(d.locator('.p-dialog-header button')).toBeEnabled();
  await page.keyboard.press('Escape');
  await expect(d).toBeHidden();
  // Successful payment refreshed the directory and detached its original opener.
  await expect(
    page.locator('[data-dialog-focus-fallback]').first(),
    await page.evaluate(() => document.activeElement?.outerHTML),
  ).toBeFocused();
  await activate(opener);
  await amount.fill('5');
  await amount.press('Tab');
  controls.paymentMode = 'reject';
  await activate(d.getByRole('button', { name: /Submit Payment$/ }));
  await expect(amount).toHaveAttribute('aria-invalid', 'true');
  await expect(amount).toHaveAccessibleDescription(/.+/);
  await expect(d).not.toContainText('unsafe raw message');
  controls.paymentMode = 'drop';
  await activate(d.getByRole('button', { name: /Submit Payment$/ }));
  await expect(d.locator('app-pending-financial-operation')).toBeVisible();
  const original = controls.paymentPosts.at(-1);
  await page.keyboard.press('Escape');
  await expect(d).toBeHidden();
  await activate(opener);
  await activate(d.getByRole('button', { name: 'Retry same payment', exact: true }));
  await expect.poll(() => controls.paymentPosts.length).toBe(4);
  expect(controls.paymentPosts.at(-1)).toEqual(original);
});
test('receiving initial/return focus, linked error and unknown Escape recovery', async ({
  page,
}) => {
  server.setMode('reject');
  const { d, opener } = await receiving(page);
  await expect(
    d.locator('[data-dialog-initial-focus]'),
    await page.evaluate(() => document.activeElement?.outerHTML),
  ).toBeFocused();
  const rows = d.getByTestId('receipt-row');
  const row = rows.first();
  await activate(d.getByRole('button', { name: 'Add another lot', exact: true }));
  const selects = rows
    .locator('p-select[formcontrolname="purchaseOrderLineId"]')
    .getByRole('combobox');
  await expect(selects).toHaveCount(2);
  await expect.soft(selects.nth(0)).toHaveAccessibleName('Line');
  await expect.soft(selects.nth(1)).toHaveAccessibleName('Line');
  const selectIds = await selects.evaluateAll((nodes) => nodes.map((el) => el.id));
  expect(new Set(selectIds).size).toBe(2);
  await activate(
    rows
      .nth(1)
      .getByRole('button', {
        name: tr('en-IN', 'purchaseOrders.receiveDialog.removeLine'),
        exact: true,
      }),
  );
  await expect(rows).toHaveCount(1);
  const receiptUrl = `**/api/purchase-orders/${PO}/receipts`;
  const fieldRejection = async (route: import('@playwright/test').Route) => {
    const response = await route.fetch();
    await route.fulfill({
      response,
      json: {
        ...(await response.json()),
        errors: [
          { code: 'Lines[0].PurchaseOrderLineId', description: 'unsafe field description' },
          { code: 'Lines[0].ExpiryDate', description: 'unsafe field description' },
          { code: 'Lines[0].ManufacturingDate', description: 'unsafe field description' },
        ],
      },
    });
  };
  await page.context().route(receiptUrl, fieldRejection);
  await row.locator('app-inventory-barcode-field input').first().fill('A11Y');
  await row.locator('[formcontrolname="batchNumber"]').fill('LOT-A11Y');
  for (const [name, value] of Object.entries({
    quantity: '2',
    totalPurchaseCost: '80',
    mrp: '60',
    salesPrice: '50',
  })) {
    const input = row.locator(`[formcontrolname="${name}"] input`).first();
    await input.fill(value);
    await input.press('Tab');
  }
  await activate(d.getByRole('button', { name: /Receive$/ }));
  await expect(row.locator('[formcontrolname="batchNumber"]')).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await expect(row.locator('[formcontrolname="batchNumber"]')).toHaveAccessibleDescription(/.+/);
  const nativeFields = [
    row.locator('p-select[formcontrolname="purchaseOrderLineId"]').getByRole('combobox'),
    row.locator('p-datepicker[formcontrolname="expiryDate"] input'),
    row.locator('p-datepicker[formcontrolname="manufacturingDate"] input'),
  ];
  for (const field of nativeFields) {
    await expect.soft(field).toHaveAttribute('aria-invalid', 'true');
    await expect
      .soft(field)
      .toHaveAccessibleDescription(tr('en-IN', 'purchaseOrders.receiving.invalidRow'));
  }
  await expect(d).not.toContainText('unsafe field description');
  await scan(page, '.p-dialog');
  await page.context().unroute(receiptUrl, fieldRejection);
  server.setMode('drop');
  await activate(d.getByRole('button', { name: /Receive$/ }));
  await expect(
    d.getByRole('status').filter({ hasText: 'Receipt outcome is unknown' }),
  ).toBeVisible();
  for (const field of nativeFields) {
    await expect(field).not.toHaveAttribute('aria-invalid', 'true');
    await expect(field).not.toHaveAttribute('aria-describedby', /.+/);
  }
  const original = server.posts.at(-1)!.raw;
  await expect(d.getByRole('button', { name: 'Retry same receipt', exact: true })).toBeEnabled();
  await page.keyboard.press('Escape');
  await expect(d).toBeHidden();
  await expect(opener).toBeFocused();
  await activate(opener);
  await expect(
    d.getByRole('status').filter({ hasText: 'Receipt outcome is unknown' }),
  ).toBeVisible();
  await activate(d.getByRole('button', { name: 'Retry same receipt', exact: true }));
  await expect.poll(() => server.posts.length).toBe(3);
  expect(server.posts.at(-1)!.raw).toBe(original);
});

test('repeated product and lot controls preserve separate line identities', async ({ page }) => {
  await pos(page);
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill('A11Y');
  await input.press('Enter');
  const picker = page.getByRole('dialog', { name: 'Select a Batch', exact: true });
  await picker.locator('.batch-option').nth(1).press('Space');
  const quantity = picker.locator('app-quantity-field input');
  await quantity.fill('1');
  await quantity.press('Tab');
  await activate(picker.getByRole('button', { name: /Add to Cart$/ }));
  await expect(picker).toBeHidden();
  const controlsInCart = page.locator('app-cart-table app-quantity-field input');
  await expect(controlsInCart).toHaveCount(2);
  const names = await controlsInCart.evaluateAll((nodes) =>
    nodes.map((el) => el.getAttribute('aria-label')),
  );
  expect(new Set(names).size).toBe(2);
  expect(names[0]).toContain('lot DUPLICATE-LOT, line 1');
  expect(names[1]).toContain('lot DUPLICATE-LOT, line 2');
  await controlsInCart.nth(1).fill('3');
  await controlsInCart.nth(1).press('Tab');
  await expect(controlsInCart.nth(0)).toHaveValue('2');
  await expect(controlsInCart.nth(1)).toHaveValue('3');
  await activate(page.getByRole('button', { name: /^Remove Premium.*line 2/ }));
  await expect(controlsInCart).toHaveCount(1);
  await expect(controlsInCart).toHaveValue('2');
  expect(controls.salePosts).toHaveLength(0);
});

test('taxed A4 summary remains readable at 320px beside the scrollable item table', async ({
  page,
}) => {
  controls.taxedPrint = true;
  await page.setViewportSize({ width: 320, height: 900 });
  await page.goto(`${server.origin}/sales/${SALE.saleId}/print?template=a4`);
  await expect(page.locator('.invoice-document')).toContainText('Frozen Tax Market');
  await expect(page.locator('.invoice__tax-summary-table tbody tr')).toHaveCount(2);
  await expect(page.locator('.invoice__tax-summary')).toContainText('₹9.00');
  await expect(page.locator('.invoice__totals')).toContainText('₹118.00');
  await scan(page, '.invoice-print-page');
  await layout(page, page.locator('.invoice-print-page'));
  mkdirSync(EVIDENCE, { recursive: true });
  await page.screenshot({ path: `${EVIDENCE}/en-IN-mobile-taxed-a4.png`, fullPage: true });
});
