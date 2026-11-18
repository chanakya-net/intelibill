import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

const commercial = {
  schemaVersion: 1, shopId: SHOP_A, evaluatedAt: '2027-02-10T00:00:00Z', state: 'Active',
  term: {
    termId: '44444444-4444-4444-8444-444444444444', state: 'Active', evidenceState: 'Complete',
    basis: 'Paid', startsAt: '2027-01-31T04:30:00Z', paidThrough: '2027-02-28T04:30:00Z',
    graceEndsAt: null, nextDeadline: null, coverageMode: 'Independent', purchasedCapacity: 1,
    memberAllowance: 5, commercial: null,
  },
  accountId: null, observeOnly: true, entitlementSynchronization: 'Applied', paymentVerification: 'NotAvailable',
  synchronization: {
    state: 'Applied', delivery: 'Acknowledged', savedAccountRevision: 2, savedShopRevision: 3,
    savedVerifiedAt: '2027-02-10T00:00:00Z', appliedAccountRevision: 2, appliedShopRevision: 3,
    lastSynchronizedAt: '2027-02-10T00:00:05Z', attention: null,
  },
};

const verified = {
  status: 200,
  json: {
    schemaVersion: 1, shopId: SHOP_A, authorityVerifiedAt: '2027-02-08T00:00:00Z',
    validUntil: '2027-02-10T00:00:00Z', enforced: false,
  },
};
const unavailable = {
  status: 503,
  json: { status: 503, code: 'Licence.VerificationUnavailable', traceId: 'fixture-outage' },
};
const expired = {
  status: 403,
  json: { status: 403, code: 'Licence.Expired', traceId: 'fixture-expiry' },
};

test.use({ serviceWorkers: 'block' });

test('recovers from verification outage and distinguishes known expiry', async ({ page }, info) => {
  const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
  try {
    const staff = session();
    staff.shops = staff.shops.map(shop => ({ ...shop, role: 'Staff' }));
    await page.addInitScript(value => {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
    }, staff);
    let response: typeof verified | typeof unavailable | typeof expired = verified;
    const reads: (typeof response)[] = [];
    await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route =>
      route.fulfill({ json: commercial }));
    await page.route(`**/api/licensing/shops/${SHOP_A}/trading-authority`, async route => {
      reads.push(response);
      await route.fulfill({ ...response, headers: { 'Cache-Control': 'no-store' } });
    });

    await page.goto(`${server.origin}/billing/status`);
    const deadline = page.getByTestId('trading-authority-deadline');
    const retry = page.getByTestId('status-retry');
    await expect(deadline).toContainText('10 Feb 2027, 05:30:00 IST');
    await expect(page.getByTestId('status-state')).toHaveText('Active');

    response = unavailable;
    await retry.click();
    await expect(page.getByTestId('trading-authority-unavailable')).toContainText(/check again.*contact support/i);
    await expect(deadline).toHaveCount(0);
    await expect(page.getByTestId('trading-authority-expired')).toHaveCount(0);
    await expect(page.getByTestId('status-state')).toHaveText('Active');
    await expect(page.getByTestId('entitlement-sync')).toBeVisible();
    await page.screenshot({ path: info.outputPath('verification-outage.png') });

    response = verified;
    await retry.click();
    await expect(deadline).toContainText('10 Feb 2027, 05:30:00 IST');
    await expect(page.getByTestId('trading-authority-unavailable')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('verification-recovered.png') });

    response = expired;
    await retry.click();
    await expect(page.getByTestId('trading-authority-expired')).toContainText('trading access has ended');
    await expect(deadline).toHaveCount(0);
    await expect(page.getByTestId('trading-authority-unavailable')).toHaveCount(0);
    await expect(page.getByTestId('commercial-price')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('known-expiry.png') });

    expect(reads.map(read => read.status)).toEqual([200, 503, 200, 403]);
    await writeFile(info.outputPath('stubbed-licensing-http.json'),
      JSON.stringify({ commercial, localAuthority: reads }, null, 2));
  } finally {
    await server.close();
  }
});

for (const width of [1440, 390]) {
  test(`P04-04 release refresh preserves the expired term at ${width}px`, async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(resolve(__dirname, '../../../dist/INVENTORY/browser'));
    const evaluatedAt = '2027-03-07T04:30:00Z';
    const restricted = { status: 403, json: { status: 403, code: 'Licence.SecuritySuspended', traceId: 'fixture-restriction' } };
    const term = { ...commercial.term, state: 'Expired', graceEndsAt: evaluatedAt, accessEndsAt: evaluatedAt };
    const reads: number[] = [];
    let applied = false;
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.addInitScript(value => {
        localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
      }, session());
      await page.route(`**/api/licensing/shops/${SHOP_A}/status`, route =>
        route.fulfill({ json: { ...commercial, term, evaluatedAt, state: 'Expired', observeOnly: false,
          ...(!applied && { enforcedSuspensionMode: 'Security' }),
          synchronization: { ...commercial.synchronization,
            state: applied ? 'Applied' : 'Pending', delivery: applied ? 'Acknowledged' : 'AwaitingAcknowledgement',
            savedAccountRevision: 2, savedShopRevision: 4, savedVerifiedAt: evaluatedAt,
            lastSynchronizedAt: applied ? evaluatedAt : commercial.synchronization.lastSynchronizedAt,
            appliedShopRevision: applied ? 4 : 3 },
        } }));
      await page.route(`**/api/licensing/shops/${SHOP_A}/trading-authority`, route => {
        const result = applied ? expired : restricted;
        reads.push(result.status);
        return route.fulfill({ ...result, headers: { 'Cache-Control': 'no-store' } });
      });
      await page.goto(`${server.origin}/billing/status`);
      const paid = page.getByTestId('paid-through');
      const grace = page.getByTestId('grace-ends');
      await expect(paid).toBeVisible();
      await expect(page.getByTestId('trading-authority-restricted')).toBeVisible();
      await expect(page.getByTestId('entitlement-sync')).toContainText('Saved, waiting for confirmation');
      const days = [await paid.textContent(), await grace.textContent()];
      await page.screenshot({ path: info.outputPath('merchant-release-pending.png'), fullPage: true });
      applied = true;
      await page.getByTestId('status-retry').click();
      await expect(page.getByTestId('trading-authority-expired')).toBeVisible();
      await expect(page.getByTestId('trading-authority-restricted')).toHaveCount(0);
      await expect(page.getByTestId('enforced-suspension')).toHaveCount(0);
      await expect(page.getByTestId('status-state')).toHaveText('Expired');
      await expect(paid).toHaveText(days[0]!);
      await expect(grace).toHaveText(days[1]!);
      expect(reads).toEqual([403, 403]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('merchant-release-expired.png'), fullPage: true });
      await writeFile(info.outputPath('fixture-licensing-http.json'),
        JSON.stringify({ term, before: restricted.json, after: expired.json }, null, 2));
    } finally {
      await server.close();
    }
  });
}
