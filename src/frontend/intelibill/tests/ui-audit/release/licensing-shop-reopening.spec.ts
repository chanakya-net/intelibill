import { resolve } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const at = '2026-11-21T04:00:00.123456Z';
const term = {
  termId: '82f02a94-6bc1-430b-aba5-80ceaf2b750d', state: 'Active', evidenceState: 'Complete', basis: 'Paid',
  billingPeriod: 'Month', timeZone: 'Asia/Kolkata', startsAt: '2026-10-31T18:30:00Z', paidThrough: '2026-11-30T18:30:00Z',
  graceEndsAt: '2026-12-07T18:30:00Z', accessEndsAt: '2026-12-07T18:30:00Z', nextDeadline: '2026-11-30T18:30:00Z',
  nextPeriodEndsAt: '2026-12-31T18:30:00Z', coverageMode: 'Independent', purchasedCapacity: 1, memberAllowance: 5,
  sourceReference: null, developmentFixture: true, commercial: null,
};

test.use({ serviceWorkers: 'block' });

// HTTP and identity replies are fixtures. Chromium serves the production merchant build.
for (const width of [1440, 390]) {
  test(`merchant reopening delivery and security suspension at ${width}px`, async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
    let stage: 'closed' | 'partial-pending' | 'partial-received' | 'pending' | 'applied' = 'closed';
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session(SHOP_A));
      await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: {
        schemaVersion: 1, shopId: SHOP_A, accountId: null, evaluatedAt: at, state: 'Active',
        term, observeOnly: false, entitlementSynchronization: 'NotAvailable', paymentVerification: 'NotAvailable',
        ...(stage === 'closed' || stage.startsWith('partial') ? { closedAt: at, closureEnforced: true } : {}),
        ...(stage !== 'closed' ? { reopenedAt: at, reopeningReceived: stage === 'partial-received' || stage === 'applied',
          ...(stage.startsWith('partial') ? {} : { reopeningEnforced: stage === 'applied' }), enforcedSuspensionMode: 'Security' } : {}),
      } }));
      await page.route(`**/api/licensing/shops/${SHOP_A}/trading-authority`, route => route.fulfill({
        status: 403, json: { code: 'Licence.SecuritySuspended' },
      }));
      await page.goto(`${server.origin}/billing/status`);
      const reopening = page.getByTestId('shop-reopening-status');
      await expect(page.getByTestId('shop-closure-status')).toBeVisible();
      await expect(reopening).toHaveCount(0);
      stage = 'partial-pending';
      await page.reload();
      await expect(reopening).toContainText('Awaiting confirmation from the merchant service');
      await expect(page.getByTestId('shop-closure-status')).toBeVisible();
      stage = 'partial-received';
      await page.reload();
      await expect(reopening).toContainText('Reopening decision received by the merchant service.');
      await expect(page.getByTestId('shop-closure-status')).toContainText('Closure is enforced for this shop.');
      await page.screenshot({ path: info.outputPath('partial-received.png'), fullPage: true });
      stage = 'pending';
      await page.reload();
      await expect(reopening).toContainText('09:30:00.123456 IST');
      await expect(reopening).toContainText('Awaiting confirmation from the merchant service');
      await expect(page.getByTestId('enforced-suspension')).toBeVisible();
      await page.screenshot({ path: info.outputPath('pending.png'), fullPage: true });
      stage = 'applied';
      await page.reload();
      await expect(reopening).toContainText('Reopening applied at this shop.');
      await expect(page.getByTestId('shop-closure-status')).toHaveCount(0);
      await expect(page.getByTestId('enforced-suspension')).toBeVisible();
      await expect(page.getByTestId('status-state')).toContainText('Active');
      await expect(page.getByText('Start a shop trial')).toHaveCount(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('applied.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}


test('observe-only shop displays receipt without an enforcement claim', async ({ page }, info) => {
  const server = await merchantServer();
  let received = false;
  try {
    await installMerchant(page);
    await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: {
      ...status(), observeOnly: true, reopenedAt: at, reopeningReceived: received,
    } }));
    await page.goto(`${server.origin}/billing/status`);
    const reopening = page.getByTestId('shop-reopening-status');
    await expect(reopening).toContainText('Reopening saved at');
    await expect(reopening).toContainText('Awaiting confirmation from the merchant service.');
    received = true;
    await page.reload();
    await expect(reopening).toContainText('Reopening decision received by the merchant service.');
    await expect(reopening).not.toContainText('Reopening applied');
    await page.screenshot({ path: info.outputPath('observe-received.png'), fullPage: true });
  } finally {
    await server.close();
  }
});

for (const unresolved of [false, true]) {
  test(`reopening ${unresolved ? 'preserves unresolved closure intent' : 'allows a new closure in the same profile'}`, async ({ page }, info) => {
    const server = await merchantServer();
    let stage: 'open' | 'closed' | 'reopened' = 'open';
    const requests: { operationKey: string; expectedVersion: number }[] = [];
    try {
      await installMerchant(page);
      await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: {
        ...status(), ...(stage === 'reopened' ? { reopenedAt: at, reopeningEnforced: true, reopeningReceived: true, reopenedVersion: 17 } : {}),
        ...(stage === 'closed' ? { closedAt: at, closureEnforced: true } : {}),
      } }));
      await page.route(`**/api/licensing/shops/${SHOP_A}/closure-preview`, route => route.fulfill({ json: {
        shopId: SHOP_A, shopName: 'Indiranagar Shop', accountId, affiliationRequestId: '52345678-1234-4234-8234-123456789abc',
        closureVersion: stage === 'reopened' ? 17 : 0, ownerVersion: 4, workDigest: 'a'.repeat(64), observedAt: at,
        openTills: 0, unresolvedCases: 0, observedSubmissions: 0, linkedSubmissions: 0, incompleteIntakes: 0,
        unreadableIntakes: 0, observedDevices: 0, devices: [], disconnectedQueuesUnknown: true,
        financialTreatment: 'NoFinancialChange',
      } }));
      await page.route(`**/api/licensing/shops/${SHOP_A}/closure`, route => {
        const body = route.request().postDataJSON();
        requests.push(body);
        return unresolved && requests.length === 1 ? route.abort('connectionreset') : route.fulfill({ json: {
          id: requests.length === 1 ? accountId : '82345678-1234-4234-8234-123456789abc',
          accountId, operationKey: body.operationKey, scope: 'Shop', financialTreatment: 'NoFinancialChange', shops: [{ shopId: SHOP_A }],
        } });
      });
      await page.goto(`${server.origin}/billing/status`);
      const panel = page.getByRole('region', { name: 'Close this shop' });
      const retry = panel.getByRole('button', { name: 'Retry the same request' });
      await closeFromPanel(panel);
      await expect(unresolved ? retry : panel.getByTestId('closure-saved')).toBeVisible();
      stage = 'closed';
      await page.reload();
      await expect(page.getByTestId('shop-closure-status')).toBeVisible();
      stage = 'reopened';
      await page.reload();
      await expect(page.getByTestId('shop-reopening-status')).toContainText('Reopening applied at this shop.');
      if (unresolved) {
        await expect(panel.getByRole('button', { name: 'Review shop closure' })).toHaveCount(0);
        await retry.click();
        await expect(panel.getByRole('button', { name: 'Review shop closure' })).toBeEnabled();
        expect(requests[1]).toEqual(requests[0]);
      } else {
        await closeFromPanel(panel);
        await expect(panel.getByTestId('closure-saved')).toBeVisible();
        expect(requests.map(request => request.expectedVersion)).toEqual([0, 17]);
        expect(requests[1].operationKey).not.toBe(requests[0].operationKey);
        await page.screenshot({ path: info.outputPath('second-closure.png'), fullPage: true });
      }
      expect(requests).toHaveLength(2);
    } finally {
      await server.close();
    }
  });
}

const accountId = '42345678-1234-4234-8234-123456789abc';

function status() {
  return { schemaVersion: 1, shopId: SHOP_A, accountId, evaluatedAt: at, state: 'Active', term,
    observeOnly: false, entitlementSynchronization: 'NotAvailable', paymentVerification: 'NotAvailable' };
}

function merchantServer() {
  return startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
}

async function installMerchant(page: Page) {
  await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session(SHOP_A));
}

async function closeFromPanel(panel: Locator) {
  await panel.getByRole('button', { name: 'Review shop closure' }).click();
  const review = panel.getByTestId('closure-review');
  await expect(review.getByRole('checkbox')).toHaveCount(3);
  for (const checkbox of await review.getByRole('checkbox').all()) await checkbox.check();
  await review.getByRole('button', { name: 'Confirm shop closure' }).click();
}
