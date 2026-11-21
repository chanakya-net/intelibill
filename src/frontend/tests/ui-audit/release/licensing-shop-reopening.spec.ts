import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
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
    let stage: 'closed' | 'pending' | 'applied' = 'closed';
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.addInitScript(value => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)), session(SHOP_A));
      await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route => route.fulfill({ json: {
        schemaVersion: 1, shopId: SHOP_A, accountId: null, evaluatedAt: at, state: 'Active',
        term, observeOnly: false, entitlementSynchronization: 'NotAvailable', paymentVerification: 'NotAvailable',
        ...(stage === 'closed' ? { closedAt: at, closureEnforced: true }
          : { reopenedAt: at, reopeningEnforced: stage === 'applied', enforcedSuspensionMode: 'Security' }),
      } }));
      await page.route(`**/api/licensing/shops/${SHOP_A}/trading-authority`, route => route.fulfill({
        status: 403, json: { code: 'Licence.SecuritySuspended' },
      }));
      await page.goto(`${server.origin}/billing/status`);
      const reopening = page.getByTestId('shop-reopening-status');
      await expect(page.getByTestId('shop-closure-status')).toBeVisible();
      await expect(reopening).toHaveCount(0);
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
