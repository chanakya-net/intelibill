import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { session, SHOP_A, SHOP_B, startPurchaseReceivingServer } from './purchase-receiving-server';

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
    const requests: unknown[] = [];
    try {
      await page.setViewportSize({ width, height: 860 });
      await mockSession(page);
      await page.route(`**/api/licensing/shops/${SHOP_A}/closure`, async route => {
        const body = route.request().postDataJSON();
        requests.push(body);
        expect(route.request().method()).toBe('POST');
        expect(body).toMatchObject({
          expectedVersion: 0, ownerVersion: 4, workDigest, reviewedKnownWork: true, consideredDeviceQueues: true,
          financialTreatment: 'NoFinancialChange', operationKey: expect.stringMatching(/^[\da-f-]{36}$/),
        });
        expect(Object.keys(body).sort()).toEqual([
          'consideredDeviceQueues', 'expectedVersion', 'financialTreatment', 'operationKey',
          'ownerVersion', 'reviewedKnownWork', 'workDigest',
        ]);
        if (requests.length === 1) return route.fulfill({ status: 503, json: { code: 'Fixture.OutcomeUnknown' } });
        if (requests.length === 2) return route.fulfill({ status: 404, json: { code: 'Fixture.OutcomeUnavailable' } });
        return route.fulfill({ json: { id: proposalId, accountId, operationKey: body.operationKey, scope: 'Shop',
          financialTreatment: 'NoFinancialChange', shops: [{ shopId: SHOP_A }] } });
      });

      await page.goto(`${server.origin}/billing/status`);
      const before = await updateQueue(page, true);
      await expect(page.getByTestId('status-state')).toBeVisible();
      await page.getByRole('button', { name: 'Review shop closure' }).click();
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
      await retry.click();
      await expect(page.getByRole('region', { name: 'Close this shop' }).getByRole('alert')).toContainText('The outcome is unconfirmed');
      await retry.click();
      await expect(page.getByTestId('closure-saved')).toContainText('Merchant enforcement may still be pending');
      expect(requests).toHaveLength(3);
      expect(requests[1]).toEqual(requests[0]);
      expect(requests[2]).toEqual(requests[0]);
      expect(await updateQueue(page, false)).toEqual(before);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('shop-saved.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}

test('affected owner consent and Billing Owner account request have distinct scope', async ({ page }) => {
  const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
  try {
    await mockSession(page);
    await page.route(`**/api/licensing/shops/${SHOP_A}/account-closure`, route => route.fulfill({ json: proposal }));
    await page.route(`**/api/licensing/shops/${SHOP_A}/account-closure/${proposalId}`, route => {
      expect(route.request().postDataJSON()).toMatchObject({ workDigest, consideredDeviceQueues: true });
      return route.fulfill({ json: { ...proposal, consentRecorded: true } });
    });
    await page.route(`**/api/billing/accounts/${accountId}/subscriptions`, route => route.fulfill({ json: {
      schemaVersion: 1, accountId, evaluatedAt: observedAt, items: [], nextCursor: null,
    } }));
    const shops = [{ shopId: SHOP_A, shopName: 'Indiranagar Shop' }, { shopId: SHOP_B, shopName: 'Jayanagar Shop' }];
    await page.route(`**/api/billing/accounts/${accountId}/closures`, route => route.fulfill({ json: {
      ...proposal, shops: shops.map(shop => ({ ...shop, consentRecorded: true })),
    } }));
    await page.route(`**/api/billing/accounts/${accountId}/closures/${proposalId}/confirm`, route => {
      const command = route.request().postDataJSON();
      expect(command).toEqual({ confirmed: true, scopeDigest: proposal.scopeDigest, operationKey: expect.any(String) });
      return route.fulfill({ json: { id: proposalId, accountId, operationKey: command.operationKey, scope: 'Account',
        financialTreatment: 'NoFinancialChange', shops } });
    });

    await page.goto(`${server.origin}/billing/status`);
    await page.getByRole('button', { name: 'Review my shop for account closure' }).click();
    const panel = page.getByTestId('closure-review');
    await expect(panel).toContainText('This account proposal covers 2 shops');
    await expect(panel).not.toContainText('Jayanagar Shop');
    await acknowledge(panel);
    await panel.getByRole('button', { name: 'Confirm this shop for account closure' }).click();
    await expect(page.getByTestId('closure-saved')).toContainText('The Billing Owner must confirm');

    await page.goto(`${server.origin}/billing/accounts/${accountId}/status`);
    await page.getByRole('button', { name: 'Review billing account closure' }).click();
    await expect(page.getByText('Jayanagar Shop · Owner confirmation recorded')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Confirm account closure' })).toBeDisabled();
    await page.getByRole('checkbox', { name: /confirm operational closure for every listed shop/ }).check();
    await page.getByRole('button', { name: 'Confirm account closure' }).click();
    await expect(page.getByText('Closure saved. Merchant enforcement may still be pending.')).toBeVisible();
  } finally {
    await server.close();
  }
});

async function mockSession(page: Page) {
  await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session());
  await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: fixtureStatus }));
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
