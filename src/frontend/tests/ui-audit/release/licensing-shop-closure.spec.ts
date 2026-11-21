import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { session, SHOP_A, SHOP_B, USER, startPurchaseReceivingServer } from './purchase-receiving-server';

const accountId = '42345678-1234-4234-8234-123456789abc';
const affiliationRequestId = '52345678-1234-4234-8234-123456789abc';
const proposalId = '62345678-1234-4234-8234-123456789abc';
const observedAt = '2026-11-21T04:00:00Z';
const workDigest = 'a'.repeat(64);

const review = {
  shopId: SHOP_A, shopName: 'Indiranagar Shop', accountId, affiliationRequestId, closureVersion: 0, ownerVersion: 4,
  observedAt, workDigest, openTills: 1, unresolvedCases: 2, observedSubmissions: 3, linkedSubmissions: 1, incompleteIntakes: 4,
  unreadableIntakes: 0, observedDevices: 1, devicesTruncated: false, disconnectedQueuesUnknown: true,
  financialTreatment: 'NoFinancialChange',
  devices: [{ reference: 'opaque', lastContactAt: observedAt, recordedLeaseEndsAt: '2026-11-23T04:00:00Z', permitEvidence: 'Unavailable' }],
};
const proposal = {
  id: proposalId, accountId, scopeDigest: 'b'.repeat(64), expiresAt: '2026-11-22T04:00:00Z',
  shopCount: 2, target: { shopId: SHOP_A, affiliationRequestId, closureVersion: 0 }, consentRecorded: false,
};
const fixtureStatus = {
  schemaVersion: 1, shopId: SHOP_A, accountId, evaluatedAt: observedAt, state: 'PendingActivation', term: null,
  observeOnly: true, entitlementSynchronization: 'NotAvailable', paymentVerification: 'NotAvailable',
};
const queueRecord = {
  key: 'fixture-disconnected-device-invoice', shopId: SHOP_A, deviceId: 'another-device', clientSaleId: 'retained-invoice',
  status: 'Pending', payload: { clientSaleId: 'retained-invoice' },
};

test.use({ serviceWorkers: 'block' });

// HTTP and identity responses are fixtures. These checks exercise the built application and real browser storage.
for (const width of [1440, 390]) {
  test(`shop owner closure review and recovery, ${width}px`, async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
    try {
      await page.setViewportSize({ width, height: 860 });
      await mockSession(page, width === 390 ? null : accountId);
      const requests = await mockShopRecovery(page, server.origin);

      await page.goto(`${server.origin}/billing/status`);
      const before = await updateQueue(page, true);
      await expect(page.getByTestId('status-state')).toBeVisible();
      const [previewRequest] = await Promise.all([
        page.waitForRequest(`${server.origin}/api/licensing/shops/${SHOP_A}/closure-preview`),
        page.getByRole('button', { name: 'Review shop closure' }).click(),
      ]);
      expect(previewRequest.headers()['authorization']).toMatch(/^Bearer .+\.fixture$/);
      const panel = page.getByTestId('closure-review');
      await expect(panel.getByRole('heading')).toHaveText('Indiranagar Shop');
      await expect(panel.getByText('Open tills', { exact: true })).toBeVisible();
      await expect(panel.getByText('21 Nov 2026', { exact: false }).first()).toBeVisible();
      await expect(panel.getByText('Recorded invoice lease end', { exact: false })).toBeVisible();
      await expect(panel.getByTestId('closure-unknown-queues')).toContainText('Disconnected devices may still have unsent work');
      await expect(panel.getByText('Other shops keep their current access', { exact: false })).toBeVisible();
      await expect(panel.getByText('Closure leaves subscriptions, payments and refunds unchanged.')).toBeVisible();
      const confirm = panel.getByRole('button', { name: 'Confirm shop closure' });
      await expect(confirm).toBeDisabled();
      await acknowledge(panel);
      await expect(confirm).toBeEnabled();
      await page.screenshot({ path: info.outputPath('shop-review.png'), fullPage: true });
      await confirm.click();
      await expect(page.getByRole('button', { name: 'Retry the same request' })).toBeVisible();

      await page.reload();
      const retry = page.getByRole('button', { name: 'Retry the same request' });
      for (const status of [404, 503]) {
        const [response] = await Promise.all([
          page.waitForResponse(`${server.origin}/api/licensing/shops/${SHOP_A}/closure`),
          retry.click(),
        ]);
        expect(response.status()).toBe(status);
        await expect(page.getByRole('region', { name: 'Close this shop' }).getByRole('alert')).toContainText('The outcome is unconfirmed');
        await page.reload();
      }
      await retry.click();
      await expect(page.getByTestId('closure-saved')).toContainText('Merchant enforcement may still be pending');
      expect(requests).toHaveLength(4);
      for (const request of requests.slice(1)) expect(request).toEqual(requests[0]);
      expect(await updateQueue(page, false)).toEqual(before);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('shop-saved.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}

async function mockShopRecovery(page: Page, origin: string) {
  const requests: unknown[] = [];
  await page.route(`**/api/licensing/shops/${SHOP_A}/closure`, async route => {
    const body = route.request().postDataJSON();
    requests.push(body);
    expect(route.request().method()).toBe('POST');
    expect(route.request().url()).toBe(`${origin}/api/licensing/shops/${SHOP_A}/closure`);
    expect(route.request().headers()['authorization']).toMatch(/^Bearer .+\.fixture$/);
    expect(body).toMatchObject({
      expectedVersion: 0, ownerVersion: 4, workDigest, reviewedKnownWork: true, consideredDeviceQueues: true,
      financialTreatment: 'NoFinancialChange', operationKey: expect.stringMatching(/^[\da-f-]{36}$/),
    });
    expect(Object.keys(body).sort()).toEqual([
      'consideredDeviceQueues', 'expectedVersion', 'financialTreatment', 'operationKey',
      'ownerVersion', 'reviewedKnownWork', 'workDigest',
    ]);
    if (requests.length === 1) return route.abort('connectionreset');
    if (requests.length === 2) return route.fulfill({ status: 404, json: { code: 'Fixture.OutcomeUnavailable' } });
    if (requests.length === 3) return route.fulfill({ status: 503, json: { code: 'Billing.DelegationUnavailable' } });
    return route.fulfill({ json: { id: proposalId, accountId, operationKey: body.operationKey, scope: 'Shop',
      financialTreatment: 'NoFinancialChange', shops: [{ shopId: SHOP_A }] } });
  });
  return requests;
}

test('affected owner consent and Billing Owner account request recover independently', async ({ page, browser }) => {
  const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
  try {
    await mockSession(page);
    await checkShopConsentRecovery(page, server.origin);
    const account = accountFixture();
    await account.install(page);
    const accountUrl = `${server.origin}/billing/accounts/${accountId}/status`;
    const retry = page.getByRole('button', { name: 'Retry the same request' });

    await page.goto(accountUrl);
    const start = page.getByRole('button', { name: 'Request owner confirmations' });
    await expect(start).toBeEnabled();
    await start.click();
    await expect(page.getByText('Jayanagar Shop · Owner confirmation recorded')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Confirm account closure' })).toBeDisabled();
    await page.getByRole('checkbox', { name: /confirm operational closure for every listed shop/ }).check();
    await page.getByRole('button', { name: 'Confirm account closure' }).click();
    await expect(retry).toBeVisible();
    await page.reload();
    await retry.click();
    const saved = 'Closure saved. Merchant enforcement may still be pending.';
    await expect(page.getByText(saved)).toBeVisible();
    expect(account.confirmations).toHaveLength(2);
    expect(account.confirmations[1]).toEqual(account.confirmations[0]);

    const fresh = await browser.newPage({ serviceWorkers: 'block' });
    try {
      await mockSession(fresh);
      await account.install(fresh);
      await fresh.goto(accountUrl);
      await expect(fresh.getByText(saved)).toBeVisible();
      await expect(fresh.getByText('Jayanagar Shop', { exact: true })).toBeVisible();
      await expect(fresh.getByRole('button', { name: 'Request owner confirmations' })).toHaveCount(0);
      expect(account.proposalCount()).toBe(1);
    } finally {
      await fresh.close();
    }
  } finally {
    await server.close();
  }
});

async function checkShopConsentRecovery(page: Page, origin: string) {
  const requests: unknown[] = [];
  await page.route(`**/api/licensing/shops/${SHOP_A}/account-closure`, route => route.fulfill({ json: proposal }));
  await page.route(`**/api/licensing/shops/${SHOP_A}/account-closure/${proposalId}`, route => {
    const command = route.request().postDataJSON();
    expect(command).toMatchObject({ workDigest, consideredDeviceQueues: true });
    requests.push(command);
    if (requests.length === 1) return route.abort('connectionreset');
    return route.fulfill({ json: { ...proposal, consentRecorded: true } });
  });
  await page.goto(`${origin}/billing/status`);
  await page.getByRole('button', { name: 'Review my shop for account closure' }).click();
  const panel = page.getByTestId('closure-review');
  await expect(panel).toContainText('This account proposal covers 2 shops');
  await expect(panel).not.toContainText('Jayanagar Shop');
  await acknowledge(panel);
  await panel.getByRole('button', { name: 'Confirm this shop for account closure' }).click();
  const retry = page.getByRole('button', { name: 'Retry the same request' });
  await expect(retry).toBeVisible();
  await page.reload();
  await retry.click();
  await expect(page.getByTestId('closure-saved')).toContainText('The Billing Owner must confirm');
  expect(requests).toHaveLength(2);
  expect(requests[1]).toEqual(requests[0]);
}

function accountFixture() {
  const confirmations: unknown[] = [];
  const shops = [{ shopId: SHOP_A, shopName: 'Indiranagar Shop' }, { shopId: SHOP_B, shopName: 'Jayanagar Shop' }];
  const accountPath = `**/api/billing/accounts/${accountId}/closures`;
  const accountProposal = { ...proposal, shops: shops.map(shop => ({ ...shop, consentRecorded: true })) };
  let closed: object | undefined;
  let pending: object | undefined;
  let proposals = 0;

  const install = async (tab: Page) => {
    await tab.route(`**/api/billing/accounts/${accountId}/subscriptions`, route => route.fulfill({ json: {
      schemaVersion: 1, accountId, evaluatedAt: observedAt, items: [], nextCursor: null,
    } }));
    await tab.route(accountPath, route => {
      if (route.request().method() === 'POST') {
        proposals++;
        pending = { ...accountProposal, operationKey: route.request().postDataJSON().operationKey };
        return route.fulfill({ json: pending });
      }
      if (closed) return route.fulfill({ json: { proposal: null, closed } });
      if (pending) return route.fulfill({ json: { proposal: pending, closed: null } });
      return route.fulfill({ status: 404, json: { code: 'Closure.ProposalUnavailable' } });
    });
    await tab.route(`${accountPath}/${proposalId}/confirm`, route => {
      const command = route.request().postDataJSON();
      expect(command).toEqual({ confirmed: true, scopeDigest: proposal.scopeDigest, operationKey: expect.any(String) });
      confirmations.push(command);
      closed = { accountId, id: proposalId, scope: 'Account', operationKey: command.operationKey, savedAt: observedAt,
        financialTreatment: 'NoFinancialChange', shops };
      if (confirmations.length === 1) return route.abort('connectionreset');
      return route.fulfill({ json: closed });
    });
  };

  return { install, confirmations, proposalCount: () => proposals };
}

test('an incomplete account-consent draft cannot send a shop closure', async ({ page }) => {
  const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
  try {
    await mockSession(page);
    const key = 'intelibill.billing.closure.v1:' + JSON.stringify([USER, SHOP_A, 'account']);
    const operationKey = '72345678-1234-4234-8234-123456789abc';
    const raw = JSON.stringify({ operationKey, settled: false, input: { accountId, command: {
      operationKey, expectedVersion: 0, ownerVersion: 4, workDigest, reviewedKnownWork: true,
      consideredDeviceQueues: true, financialTreatment: 'NoFinancialChange',
    } } });
    await page.addInitScript(({ key, raw }) => localStorage.setItem(key, raw), { key, raw });
    const mutations: string[] = [];
    await page.route(`**/api/licensing/shops/${SHOP_A}/*closure**`, route => {
      if (route.request().method() !== 'POST') return route.fallback();
      mutations.push(route.request().url());
      return route.fulfill({ status: 409, json: { code: 'Closure.ReviewChanged' } });
    });
    await page.goto(`${server.origin}/billing/status`);
    const panel = page.getByRole('region', { name: 'Billing account closure' });
    await panel.getByRole('button', { name: 'Retry the same request' }).click();
    await expect(panel.getByRole('alert')).toBeVisible();
    expect(mutations).toEqual([]);
    await expect(panel.getByRole('alert')).toContainText('could not be stored safely');
    expect(await page.evaluate(key => localStorage.getItem(key), key)).toBe(raw);
  } finally {
    await server.close();
  }
});

async function mockSession(page: Page, visibleAccount: string | null = accountId) {
  await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session());
  await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: { ...fixtureStatus, accountId: visibleAccount } }));
  await page.route(`**/api/licensing/shops/${SHOP_A}/trading-authority`, route => route.fulfill({ status: 403, json: {
    code: 'Licence.PendingActivation', status: 403,
  } }));
  await page.route(`**/api/licensing/shops/${SHOP_A}/closure-preview`, route => route.fulfill({ json: review }));
}

async function acknowledge(panel: ReturnType<Page['getByTestId']>) {
  await panel.getByRole('checkbox', { name: /reviewed the work/ }).check();
  await panel.getByRole('checkbox', { name: /considered unsent work/ }).check();
  await panel.getByRole('checkbox', { name: /financial changes require a separate decision/ }).check();
}

function updateQueue(page: Page, seed: boolean) {
  return page.evaluate(({ seed, row }) => new Promise((resolve, reject) => {
    const request = indexedDB.open('intelibill-offline-sales-queue', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('queued-sales', { keyPath: 'key' });
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const tx = db.transaction('queued-sales', seed ? 'readwrite' : 'readonly');
      const store = tx.objectStore('queued-sales');
      if (seed) store.put(row);
      const read = store.get(row.key);
      tx.oncomplete = () => { db.close(); resolve(read.result); };
      tx.onerror = () => { db.close(); reject(tx.error); };
    };
  }), { seed, row: queueRecord });
}
