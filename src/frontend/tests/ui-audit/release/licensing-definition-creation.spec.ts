import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';
import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const ITEM = {
  id: 'item-1', name: 'Fixture widget', barcode: 'EXISTING-1', description: null,
  uom: 'pcs', isActive: true, quantityScale: 0, unitCode: 'PCS', currentStock: 3,
  unitPrice: 20, currentStockValue: 60, reorderLevel: 0, stockStatus: 'healthy',
  defaultTaxRatePercent: 0, defaultTaxIncluded: false, unitReviewRequired: false,
};
const SERVICE = {
  serviceId: 'service-1', code: 'SRV-000001', name: 'Fixture repair',
  price: 20, taxRatePercent: 0, taxIncluded: false, isActive: true,
};
const MESSAGE = {
  pending: 'This shop is waiting for trading access.',
  expired: 'This shop’s trading access has ended.',
  restricted: 'Trading is restricted for this shop.',
  outage: 'Trading access could not be verified.',
};
const REFUSALS = {
  pending: { status: 403, code: 'Licence.PendingActivation' },
  expired: { status: 403, code: 'Licence.Expired' },
  restricted: { status: 403, code: 'Licence.TradingRestricted' },
  outage: { status: 503, code: 'Licence.VerificationUnavailable' },
};
const DESKTOP = { width: 1440, height: 900 };
const MOBILE = { width: 390, height: 844 };

type Denial = keyof typeof REFUSALS;
type Phase = 'verified' | 'observe-only' | Denial;
type Event = { method: string; path: string; status: number; code?: string; barcode?: string; enforced?: boolean };

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });
}

function logResponse(log: Event[], route: Route, url: URL, status: number, fields: Partial<Event> = {}) {
  log.push({ method: route.request().method(), path: url.pathname, status, ...fields });
}

async function installFixture(page: Page) {
  const state = { hint: 'verified' as Phase, writeDenial: null as Denial | null };
  const log: Event[] = [];
  const items = [{ ...ITEM }];
  const services = [{ ...SERVICE }];
  let barcodeAllocations = 0;

  await page.route('**/api/licensing/shops/*/trading-authority', (route) => {
    const url = new URL(route.request().url());
    if (state.hint === 'verified') {
      logResponse(log, route, url, 200);
      return json(route, { schemaVersion: 1, shopId: SHOP_A, enforced: true,
        authorityVerifiedAt: '2026-11-18T20:00:00Z', validUntil: '2026-11-20T20:00:00Z' });
    }
    const enforced = state.hint !== 'observe-only';
    const denial = enforced ? REFUSALS[state.hint as Denial] : REFUSALS.expired;
    logResponse(log, route, url, denial.status, { enforced, code: denial.code });
    return json(route, { ...denial, enforced }, denial.status);
  });
  await page.route(/\/api\/items\/stream(?:\?|$)/, (route) => route.fulfill({
    contentType: 'application/x-ndjson',
    body: items.map((item) => JSON.stringify({ itemId: item.id, name: item.name, barcode: item.barcode })).join('\n') + '\n',
  }));
  await page.route(/\/api\/items\/details(?:\?|$)/, (route) => {
    const url = new URL(route.request().url());
    const barcode = url.searchParams.get('barcode') ?? undefined;
    const item = items.find((entry) => entry.barcode === barcode);
    if (!item) return refuse(log, route, url, state.writeDenial ?? 'restricted', barcode);
    logResponse(log, route, url, 200, { barcode });
    return json(route, { name: item.name, uom: item.uom, description: '', mrp: 25,
      salesPrice: 20, costPrice: 10, supplierId: null, supplierName: null, taxRatePercent: 0,
      taxIncluded: false, purchaseTaxIncluded: false, hsnCode: null });
  });
  await page.route('**/api/items/barcodes/generate', (route) => {
    const url = new URL(route.request().url());
    logResponse(log, route, url, 200);
    return json(route, { barcode: 'GEN-FIXTURE-' + ++barcodeAllocations });
  });
  await page.route(/\/api\/items(?:\?|$)/, (route) => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'GET') return json(route, {
      items, totalCount: items.length, pageNumber: 1, pageSize: 20,
      summary: { totalItems: items.length, activeItems: items.length, inactiveItems: 0,
        totalStockValue: 60, runningLowStockCount: 0, criticalStockCount: 0 },
    });
    if (state.writeDenial) return refuse(log, route, url, state.writeDenial);
    const item = { ...ITEM, ...route.request().postDataJSON(), id: 'item-' + (items.length + 1) };
    items.push(item);
    logResponse(log, route, url, 200);
    return json(route, item);
  });
  await page.route(/\/api\/services(?:\?|$)/, (route) => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'GET') return json(route, services);
    if (state.writeDenial) return refuse(log, route, url, state.writeDenial);
    const service = { ...SERVICE, ...route.request().postDataJSON(), serviceId: 'service-' + (services.length + 1) };
    services.push(service);
    logResponse(log, route, url, 201);
    return json(route, service, 201);
  });
  await page.route('**/api/hsn/**', (route) => json(route, { hsnCodes: [], taxScenarios: [] }));

  return {
    state, log, posts: () => log.filter((event) => event.method === 'POST'),
    counts: () => ({ newItems: items.length - 1, newServices: services.length - 1, barcodeAllocations }),
  };
}

function refuse(log: Event[], route: Route, url: URL, phase: Denial, barcode?: string) {
  const denial = REFUSALS[phase];
  logResponse(log, route, url, denial.status, { code: denial.code, barcode });
  return json(route, denial, denial.status);
}

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let fixture: Awaited<ReturnType<typeof installFixture>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  fixture = await installFixture(page);
  await page.addInitScript((value) => {
    if (!localStorage.getItem('inventory.auth.session.local'))
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
  }, session());
});

test.afterEach(async ({ page, browser }, info) => {
  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(info.outputPath('fixture-definition-http.json'), JSON.stringify({
    source: 'Local API fixtures with production-built merchant assets',
    role: 'Owner', shop: SHOP_A, viewport: page.viewportSize(), browser: browser.version(),
    requests: fixture.log, fixtureWrites: fixture.counts(),
  }, null, 2) + '\n');
  await server.close();
});

const addProduct = (container: Page | Locator) => container.getByRole('button', { name: 'Add New Product', exact: true });
const addService = (container: Page | Locator) => container.getByRole('button', { name: 'Add Service', exact: true });
const quickCreate = (page: Page) => page.getByRole('button', { name: 'Quick Create Product', exact: true });
const checkAgain = (container: Page | Locator) => container.getByRole('button', { name: 'Check again', exact: true });
const notice = (container: Page | Locator) => container.locator('app-definition-access-notice').getByRole('alert');

async function openPurchaseLine(page: Page) {
  await page.goto(server.origin + '/inventory/purchase-orders');
  await page.locator('.po-list-header__new-button').click();
  return page.locator('app-purchase-order-line-form p-autocomplete input');
}

test('P04-08a pending shops can read directories and select an existing purchase order item', async ({ page }, info) => {
  fixture.state.hint = 'pending';
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/inventory');
  await expect(notice(page)).toContainText(MESSAGE.pending);
  await expect(addProduct(page)).toBeDisabled();
  await expect(page.getByRole('row').getByText(ITEM.name, { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('pending-inventory-desktop.png'), animations: 'disabled' });

  await page.goto(server.origin + '/services');
  await expect(addService(page)).toBeDisabled();
  await expect(page.getByRole('row').getByText(SERVICE.name, { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('pending-services-desktop.png'), animations: 'disabled' });

  // P04-07 disables new purchase order creation while pending, keeping existing POs readable.
  await page.goto(server.origin + '/inventory/purchase-orders');
  await expect(notice(page)).toContainText(MESSAGE.pending);
  await expect(page.locator('.po-list-header__new-button')).toBeDisabled();
  await expect(page.getByRole('cell', { name: 'PO-RECEIVING-1' })).toBeVisible();
  expect(fixture.posts()).toHaveLength(0);
  await page.screenshot({ path: info.outputPath('pending-existing-po-line.png'), animations: 'disabled' });
});

test('P04-08a stale item form keeps its values through expiry, outage and verified recovery', async ({ page }, info) => {
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/inventory');
  await addProduct(page).click();
  const form = page.getByRole('dialog');
  await form.locator('#add-product-name').fill('Prepared product');
  await form.locator('app-inventory-barcode-field input').fill('PREPARED-1');
  await form.locator('#add-product-uom').fill('pcs');
  await expect(addProduct(form)).toBeEnabled();

  fixture.state.writeDenial = 'expired';
  await addProduct(form).click();
  await expect(notice(form)).toContainText(MESSAGE.expired);
  await expect(addProduct(form)).toBeDisabled();
  await expect(form.locator('#add-product-name')).toHaveValue('Prepared product');
  await page.screenshot({ path: info.outputPath('item-refused-desktop.png'), animations: 'disabled' });

  fixture.state.hint = 'outage';
  await checkAgain(form).click();
  await expect(notice(form)).toContainText(MESSAGE.outage);
  await expect(notice(form)).not.toContainText(MESSAGE.expired);
  await page.screenshot({ path: info.outputPath('item-outage-desktop.png'), animations: 'disabled' });
  expect(fixture.counts().newItems).toBe(0);

  fixture.state.hint = 'verified';
  fixture.state.writeDenial = null;
  await checkAgain(form).click();
  await expect(addProduct(form)).toBeEnabled();
  await addProduct(form).click();
  await expect(form).toHaveCount(0);
  await expect(page.getByRole('row').getByText('Prepared product', { exact: true })).toBeVisible();
  expect(fixture.posts()).toMatchObject([
    { path: '/api/items', status: 403, code: 'Licence.Expired' },
    { path: '/api/items', status: 200 },
  ]);
  expect(fixture.counts().newItems).toBe(1);
});

test('P04-08a stale service refusal stays on the mobile form and observe-only allows creation', async ({ page }, info) => {
  await page.setViewportSize(MOBILE);
  await page.goto(server.origin + '/services');
  await addService(page).click();
  const form = page.getByRole('dialog');
  await form.locator('#add-service-name').fill('Prepared repair');
  await form.locator('#add-service-price').fill('35');
  await form.locator('#add-service-price').press('Tab');

  fixture.state.writeDenial = 'restricted';
  await addService(form).click();
  await expect(notice(form)).toContainText(MESSAGE.restricted);
  await expect(addService(form)).toBeDisabled();
  await expect(form.locator('#add-service-name')).toHaveValue('Prepared repair');
  await page.screenshot({ path: info.outputPath('service-refused-mobile.png'), animations: 'disabled' });
  expect(fixture.counts().newServices).toBe(0);

  fixture.state.hint = 'observe-only';
  fixture.state.writeDenial = null;
  await checkAgain(form).click();
  await expect(addService(form)).toBeEnabled();
  await addService(form).click();
  await expect(form).toHaveCount(0);
  expect(fixture.posts()).toMatchObject([
    { path: '/api/services', status: 403, code: 'Licence.TradingRestricted' },
    { path: '/api/services', status: 201 },
  ]);
  expect(fixture.counts().newServices).toBe(1);
});

test('P04-08a quick-create honours the typed POST refusal and keeps the line draft', async ({ page }, info) => {
  await page.setViewportSize(DESKTOP);
  const description = await openPurchaseLine(page);
  const lineForm = page.locator('app-purchase-order-line-form');
  await description.fill('Prepared purchase product');
  fixture.state.writeDenial = 'pending';
  await quickCreate(page).click();
  await expect(notice(lineForm)).toContainText(MESSAGE.pending);
  await expect(quickCreate(page)).toBeDisabled();
  await expect(description).toHaveValue('Prepared purchase product');
  await expect(page.locator('app-purchase-order-lines-table')).not.toContainText('Prepared purchase product');
  expect(fixture.posts()).toMatchObject([
    { path: '/api/items/barcodes/generate', status: 200 },
    { path: '/api/items', status: 403, code: 'Licence.PendingActivation' },
  ]);
  expect(fixture.counts()).toEqual({ newItems: 0, newServices: 0, barcodeAllocations: 1 });
  // Even after pending refusal disables quick-create, an existing item can still be selected on the open line form.
  await description.fill('');
  await description.pressSequentially('Fixture wid', { delay: 35 });
  await page.getByRole('option', { name: ITEM.name }).click();
  await expect(quickCreate(page)).toBeDisabled();
  await page.getByRole('button', { name: 'Add line', exact: true }).click();
  await expect(page.locator('app-purchase-order-lines-table')).toContainText(ITEM.name);
  await page.screenshot({ path: info.outputPath('quick-create-refused-desktop.png'), animations: 'disabled' });
});

test('P04-08a batch lookup shows restricted import while a local lookup can still fill the row', async ({ page }, info) => {
  await page.setViewportSize(MOBILE);
  fixture.state.writeDenial = 'restricted';
  await page.goto(server.origin + '/inventory/batch');
  const field = page.locator('app-inventory-barcode-field').first();
  const barcode = field.locator('input');
  await barcode.fill('MISSING-1');
  await barcode.press('Tab');
  await expect(field.getByRole('status')).toContainText(MESSAGE.restricted);
  await expect(barcode).toHaveValue('MISSING-1');
  await page.screenshot({ path: info.outputPath('barcode-import-refused-mobile.png'), animations: 'disabled' });

  await barcode.fill(ITEM.barcode);
  await barcode.press('Tab');
  await expect(page.locator('p-autocomplete[formControlName="itemName"] input')).toHaveValue(ITEM.name);
  await expect(field.getByRole('status')).toHaveCount(0);
  expect(fixture.log.filter((event) => event.path === '/api/items/details')).toMatchObject([
    { barcode: 'MISSING-1', status: 403, code: 'Licence.TradingRestricted' },
    { barcode: ITEM.barcode, status: 200 },
  ]);
  expect(fixture.counts().newItems).toBe(0);
});


test('P04-08a optional cart enrichment shows its refusal without losing the sale line', async ({ page }, info) => {
  await accessibilityFixtures(page);
  fixture.state.writeDenial = 'restricted';
  await page.setViewportSize(DESKTOP);
  await page.goto(server.origin + '/sales/new');
  const input = page.locator('app-batch-search-bar input').first();
  await input.fill('A11Y');
  await input.press('Enter');
  const picker = page.getByRole('dialog', { name: /Select a Batch/i });
  await picker.locator('.batch-option').first().press('Enter');
  await picker.getByRole('button', { name: /Add to Cart$/i }).click();

  await expect(page.getByRole('status').filter({ hasText: MESSAGE.restricted })).toBeVisible();
  await expect(page.locator('app-cart-table app-quantity-field input')).toHaveValue('1');
  await expect(page.locator('app-cart-checkout-summary button').last()).toBeEnabled();
  expect(fixture.posts()).toHaveLength(0);
  await page.screenshot({ path: info.outputPath('optional-cart-enrichment-desktop.png'), animations: 'disabled' });
});
