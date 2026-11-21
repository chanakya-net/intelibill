import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

import { SALE } from './accessibility-localization-fixtures';
import { INVOICE_A, INVOICE_B, PAYMENT_INTENTS_KEY, startLicensingHistoryFixture } from './licensing-history-security-fixture';
import { shopRestrictionKey } from '../../../src/app/core/context/shop-scope';
import { session, SHOP_A, USER } from './purchase-receiving-server';
import { PROTECTED_WORKER_PROTOCOL, PROTECTED_WORKER_QUERY } from '../../../src/app/core/service-worker/service-worker-registration';

const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const restricted = (page: Page) => page.getByTestId('shop-data-restricted');

test('issued history, printing and downloads follow the observed shop restriction', async ({ page, context }, info) => {
  const api = await startLicensingHistoryFixture(BUILD);
  const origin = api.server.origin;
  const downloads: string[] = [];
  try {
    await context.addInitScript((fixtureSession) => {
      const key = 'inventory.auth.session.local';
      if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(fixtureSession));
      window.print = () => document.documentElement.setAttribute('data-printed', 'true');
    }, session());

    await page.goto(origin + '/sales');
    await expect(page.getByText(INVOICE_A, { exact: true })).toBeVisible();
    await waitForWorker(page);
    await expect(restricted(page)).toHaveCount(0);

    const a4 = await context.newPage();
    await a4.goto(`${origin}/sales/${SALE.saleId}/print?template=a4`);
    await expect(a4.locator('app-sale-invoice-a4')).toContainText(INVOICE_A);
    await expect(a4.locator('html')).toHaveAttribute('data-printed', 'true');
    await a4.screenshot({ path: info.outputPath('expired-issued-a4.png') });
    const thermal = await context.newPage();
    await thermal.goto(`${origin}/sales/${SALE.saleId}/print?template=thermal`);
    await expect(thermal.locator('app-sale-invoice-thermal')).toContainText(INVOICE_A);
    await expect(thermal.locator('html')).toHaveAttribute('data-printed', 'true');
    await thermal.screenshot({ path: info.outputPath('expired-issued-thermal.png') });
    await thermal.close();

    await page.bringToFront();
    page.on('download', (download) => downloads.push(download.suggestedFilename()));
    const file = page.waitForEvent('download');
    await exportPdf(page);
    const download = await file;
    const downloadPath = info.outputPath(download.suggestedFilename());
    await download.saveAs(downloadPath);
    expect(await readFile(downloadPath, 'utf8')).toContain(INVOICE_A);

    api.state.holdExport = true;
    await exportPdf(page);
    await expect.poll(() => api.heldExports.length).toBe(1);
    api.state.restricted = true;
    await refreshAuthority(page);
    await expect(restricted(page)).toBeVisible();
    await expect(restricted(a4)).toBeVisible();
    await expect(a4.locator('app-sale-invoice-a4')).toHaveCount(0);
    const delayed = page.waitForResponse((response) => response.url().includes('/api/exports/sales') && response.ok());
    api.heldExports[0]();
    expect((await delayed).headers()['cache-control']).toBe('no-store');
    await page.evaluate(() => new Promise<void>((resolve) => requestAnimationFrame(() => resolve())));
    await expect(page.getByText(INVOICE_A, { exact: true })).toHaveCount(0);
    expect(downloads).toEqual(['issued-history.pdf']);
    await page.screenshot({ path: info.outputPath('security-history-hidden.png') });

    const protectedEntries = await page.evaluate(async () => {
      const inventory = await Promise.all((await caches.keys()).map(async (name) =>
        (await (await caches.open(name)).keys()).map((entry) => new URL(entry.url).pathname)));
      return inventory.flat().filter((path) => path.startsWith('/api/') || path.startsWith('/hubs/'));
    });
    expect(protectedEntries).toEqual([]);
    expect(api.server.requests.some((request) => request.path === '/api/exports/sales' &&
      request.workerBypass === 'true' && !!request.authorization)).toBe(true);

    await switchShop(page, 'Shop A', 'Shop B');
    await page.goto(origin + '/sales');
    await expect(page.getByText(INVOICE_B, { exact: true })).toBeVisible();
    await expect(restricted(page)).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('unrelated-shop-history.png') });
  } finally {
    await api.server.close();
  }
});

test('an unconfirmed payment delivery remains bound to its original shop through restriction and release', async ({ page }, info) => {
  const api = await startLicensingHistoryFixture(BUILD);
  const origin = api.server.origin;
  try {
    await page.addInitScript((fixtureSession) => {
      const key = 'inventory.auth.session.local';
      if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(fixtureSession));
    }, session());
    await page.goto(origin + '/customers');
    const dialog = await openCustomer(page);
    const amount = dialog.locator('[formcontrolname="amount"] input');
    await amount.fill('25');
    await amount.blur();
    await dialog.getByRole('button', { name: /Submit Payment$/ }).click();
    await expect(dialog.getByText('Payment not confirmed', { exact: true })).toBeVisible();
    const before = await paymentIntents(page);
    const { operationKey, ...sentPayload } = api.posts[0];
    expect(Object.values(before)).toEqual([expect.objectContaining({
      operationKey, status: 'unknown', shopId: SHOP_A,
      userId: USER, payload: { ...sentPayload, tillOwner: { shopId: SHOP_A, userId: USER } },
    })]);

    api.state.restricted = true;
    await refreshAuthority(page);
    await expect(restricted(page)).toBeVisible();
    await expect(dialog).toHaveCount(0);
    api.state.verificationUnavailable = true;
    await page.reload();
    await expect(restricted(page)).toBeVisible();
    await switchShop(page, 'Shop A', 'Shop B');
    await page.goto(origin + '/customers');
    const other = await openCustomer(page);
    await expect(other.getByRole('button', { name: 'Check status', exact: true })).toHaveCount(0);
    expect(await paymentIntents(page)).toEqual(before);
    await other.getByRole('button', { name: 'Close', exact: true }).click();

    await switchShop(page, 'Shop B', 'Shop A');
    await page.goto(origin + '/customers');
    await expect(restricted(page)).toBeVisible();
    api.state.restricted = false;
    api.state.verificationUnavailable = false;
    await refreshAuthority(page);
    await expect(restricted(page)).toHaveCount(0);
    const reopened = await openCustomer(page);
    await reopened.getByRole('button', { name: 'Check status', exact: true }).click();
    await expect(reopened.getByText(/Payment confirmed\. The ledger has been refreshed/)).toBeVisible();
    await expect(reopened.getByTestId('payment-receipts').getByRole('row').filter({ hasText: 'Posted' })).toHaveCount(1);
    expect(await paymentIntents(page)).toEqual({});
    expect(api.posts).toHaveLength(1);
    expect(api.lookups).toEqual([api.posts[0]['operationKey']]);
    await page.screenshot({ path: info.outputPath('original-payment-confirmed.png') });
  } finally {
    await api.server.close();
  }
});

test('a sibling restriction retires queued printing until local authority confirms release', async ({ page, context }, info) => {
  const api = await startLicensingHistoryFixture(BUILD);
  const origin = api.server.origin;
  const marker = shopRestrictionKey({ userId: USER, shopId: SHOP_A });
  try {
    await context.addInitScript((fixtureSession) => {
      const key = 'inventory.auth.session.local';
      if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(fixtureSession));
      window.print = () => document.documentElement.setAttribute('data-printed', 'true');
    }, session());

    await page.goto(origin + '/sales');
    await expect(page.getByText(INVOICE_A, { exact: true })).toBeVisible();
    const print = await context.newPage();
    await print.setExtraHTTPHeaders({ 'x-fixture-print-tab': 'true' });
    await print.addInitScript(() => {
      let allowFonts!: () => void;
      Object.defineProperty(document.fonts, 'ready', {
        value: new Promise<void>((resolve) => { allowFonts = resolve; }),
      });
      window.addEventListener('fixture-fonts-ready', allowFonts);
    });
    api.state.holdPrintAuthority = true;
    await print.goto(`${origin}/sales/${SALE.saleId}/print?template=a4`);
    await expect(print.locator('app-sale-invoice-a4')).toContainText(INVOICE_A);
    await expect.poll(() => api.heldAuthority.length).toBe(1);
    await expect(print.locator('html')).not.toHaveAttribute('data-printed');

    api.state.printVerificationUnavailable = true;
    api.state.restricted = true;
    await page.bringToFront();
    await page.reload();
    await expect(restricted(page)).toBeVisible();
    await expect(restricted(print)).toBeVisible();
    await expect(print.locator('app-sale-invoice-a4')).toHaveCount(0);

    api.heldAuthority.shift()!();
    await print.evaluate(() => window.dispatchEvent(new Event('fixture-fonts-ready')));
    await expect(print.locator('html')).not.toHaveAttribute('data-printed');
    expect(await print.evaluate((key) => localStorage.getItem(key), marker)).toBeTruthy();
    await print.screenshot({ path: info.outputPath('sibling-notice-print-retired.png') });

    api.state.restricted = false;
    await refreshAuthority(page);
    await expect(restricted(page)).toHaveCount(0);
    await refreshAuthority(print);
    await expect(restricted(print)).toBeVisible();
    await expect(print.locator('app-sale-invoice-a4')).toHaveCount(0);

    api.state.printVerificationUnavailable = false;
    await refreshAuthority(print);
    await expect(print.locator('app-sale-invoice-a4')).toContainText(INVOICE_A);
    await expect(print.locator('html')).toHaveAttribute('data-printed', 'true');
  } finally {
    for (const send of api.heldAuthority) send();
    await api.server.close();
  }
});

async function refreshAuthority(page: Page) {
  await page.bringToFront();
  await page.evaluate(() => window.dispatchEvent(new Event('online')));
}

async function exportPdf(page: Page) {
  await page.locator('[data-export-trigger]').click();
  await page.getByRole('menuitem', { name: 'PDF' }).click();
}

async function switchShop(page: Page, from: string, to: string) {
  await page.locator('.active-shop-trigger:visible').filter({ hasText: from }).click();
  await page.getByRole('menuitem', { name: new RegExp(to) }).click();
  await expect(page.locator('.active-shop-trigger:visible')).toContainText(to);
}

async function openCustomer(page: Page) {
  await page.locator('.customer-name-link:visible, .customer-card__identity:visible').first().click();
  return page.getByRole('dialog', { name: 'Customer Account', exact: true });
}

async function paymentIntents(page: Page): Promise<Record<string, unknown>> {
  return page.evaluate((key) => JSON.parse(localStorage.getItem(key) ?? '{}'), PAYMENT_INTENTS_KEY);
}

async function waitForWorker(page: Page) {
  await page.waitForFunction(() => navigator.serviceWorker.controller?.scriptURL.endsWith('/ngsw-worker.js'));
  await expect.poll(() => page.evaluate(({ action, protocol }) => new Promise<boolean>((resolveQuery) => {
    const channel = new MessageChannel();
    const timer = setTimeout(() => resolveQuery(false), 1000);
    channel.port1.onmessage = (event) => {
      clearTimeout(timer);
      resolveQuery(event.data?.protectedApiIsolation === protocol);
    };
    navigator.serviceWorker.controller?.postMessage({ action }, [channel.port2]);
  }), { action: PROTECTED_WORKER_QUERY, protocol: PROTECTED_WORKER_PROTOCOL })).toBe(true);
}
