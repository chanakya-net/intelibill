import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { startStaticServer } from './static-server';

const account = 'b1111111-1111-4111-8111-111111111111';
const other = 'b2222222-2222-4222-8222-222222222222';
const shopId = 'b5555555-5555-4555-8555-555555555555';
const otherShop = 'b6666666-6666-4666-8666-666666666666';
const evaluatedAt = '2026-11-22T04:00:00Z';
const device = (over: object) => ({
  shopId, shopName: 'Counter Shop', deviceRef: 'ab'.repeat(32), lastContactAt: '2026-11-21T06:30:00.000123Z',
  lastSynchronizedAt: '2026-11-21T06:30:00.000123Z', lastPermitIssuedAt: '2026-11-21T04:00:00Z', outstanding: true,
  knownPermitEnd: '2026-11-23T03:59:59.999999Z', outstandingPermits: 1, limit: 'VerificationFreshness',
  accountRevision: 2, shopRevision: 3, authorityVerifiedAt: '2026-11-21T03:59:59.999999Z', ...over,
});
const ended = device({ deviceRef: 'cd'.repeat(32), shopId: otherShop, shopName: 'Back Office Shop', outstanding: false,
  outstandingPermits: 0, knownPermitEnd: '2026-11-22T04:00:00Z', lastSynchronizedAt: null, lastContactAt: '2026-11-20T04:00:00Z',
  lastPermitIssuedAt: '2026-11-20T04:00:00Z', limit: 'LicenceEnd' });

// The provider session and HTTP facts are fixtures; the Angular assets are the actual production build.
// This proves what the built panel shows for those facts, not what a live provider or device would report.
for (const width of [1440, 390]) {
  test(`device permission panel at ${width}px`, async ({ page }, info) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    const problems: string[] = [];
    const reads: URL[] = [];
    let capabilities = ['support'];
    page.on('console', message => { if (message.type() === 'error') problems.push(message.text()); });
    page.on('pageerror', error => problems.push(error.message));
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId: 'b3333333-3333-4333-8333-333333333333', sessionId: 'b4444444-4444-4444-8444-444444444444',
        capabilities, accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/accounts/*/status', route => route.fulfill({ json: { evaluatedAt, nextCursor: null,
        items: new URL(route.request().url()).pathname.includes(other) ? [] : [{ coveredShopCount: 2, coveredShops: [
          { shopId, shopName: 'Counter Shop' }, { shopId: otherShop, shopName: 'Back Office Shop' }],
        status: { termId: 'b7777777-7777-4777-8777-777777777777', state: 'Active', evidenceState: 'Complete', basis: 'Paid',
          billingPeriod: 'Month', timeZone: 'Asia/Kolkata', startsAt: '2026-11-01T00:00:00Z', paidThrough: '2026-12-01T00:00:00Z',
          graceEndsAt: null, accessEndsAt: '2026-12-01T00:00:00Z', nextDeadline: '2026-12-01T00:00:00Z', nextPeriodEndsAt: null,
          coverageMode: 'Grouped', purchasedCapacity: 2, memberAllowance: 5, sourceReference: null, developmentFixture: true,
          commercial: null } }] } }));
      await page.route('**/api/admin/accounts/*/closures', route => route.fulfill({ json: [] }));
      await page.route('**/api/admin/accounts/*/device-permits**', route => {
        const url = new URL(route.request().url());
        reads.push(url);
        expect(route.request().method()).toBe('GET');
        if (url.pathname.includes(other))
          return route.fulfill({ json: { accountId: other, evaluatedAt, disconnectedQueues: 'Unknown', observedSince: null,
            devices: [], nextCursor: null } });
        const shop = url.searchParams.get('shop');
        return route.fulfill({ json: { accountId: account, evaluatedAt, disconnectedQueues: 'Unknown',
          observedSince: '2026-11-20T04:00:00Z', devices: [device({}), ended].filter(row => !shop || row.shopId === shop),
          nextCursor: null, invoiceNumber: 'A/2627/000042', customerName: 'private-customer', deviceId: 'private-device' } });
      });

      await page.goto(server.origin + '/accounts');
      const entry = page.getByLabel('Billing account ID');
      const read = page.getByRole('button', { name: 'Read access status' });
      await entry.fill(account);
      await read.click();
      const panel = page.getByTestId('device-permit-panel');
      await expect(panel.getByTestId('device-permit-device')).toHaveCount(2);

      // Exact end and remaining time come from the server's evaluation instant; the upload time is its own fact.
      await expect(panel.getByTestId('device-permit-end')).toHaveText([
        /23 Nov 2026, 09:29:59\.999999 IST · 23 h 59 min remaining at evaluation/,
        /No known outstanding permission; last ended 22 Nov 2026, 09:30:00 IST/,
      ]);
      await expect(panel.getByTestId('device-permit-sync')).toHaveText(['21 Nov 2026, 12:00:00.000123 IST', 'None observed']);
      await expect(panel.getByTestId('device-permit-issued')).toHaveText(['21 Nov 2026, 09:30:00 IST', '20 Nov 2026, 09:30:00 IST']);
      await expect(panel.getByTestId('device-permit-state')).toHaveText(['Permission outstanding', 'No known outstanding permission']);
      await expect(panel.getByTestId('device-permit-queue')).toHaveText(['Unknown', 'Unknown']);
      await expect(panel.getByTestId('device-permit-since')).toContainText('Reporting for this account began 20 Nov 2026, 09:30:00 IST');

      // Observed, not observed and unknown are separate, and nothing claims instant revocation.
      await expect(panel.getByTestId('device-permit-observed')).toContainText("merchant service's clock");
      await expect(panel.getByTestId('device-permit-not-observed')).toContainText('Whether a device is connected now');
      await expect(panel.getByTestId('device-permit-unknown')).toContainText('does not mean its queue is empty');
      await expect(panel.getByTestId('device-permit-revocation')).toContainText('does not revoke permission already issued');
      await expect(panel.getByText(/private-customer|private-device|A\/2627/)).toHaveCount(0);
      await expect(panel.getByRole('button')).toHaveCount(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('device-permits.png'), fullPage: true });

      // Shop scope: the filter asks the server for that shop only.
      await panel.getByLabel('Shop').selectOption(otherShop);
      await expect(panel.getByTestId('device-permit-device')).toHaveCount(1);
      await expect(panel.getByTestId('device-permit-device')).toContainText('Back Office Shop');
      expect(reads.at(-1)!.searchParams.get('shop')).toBe(otherShop);

      // Account scope: another account shows none of the first account's devices, and absence is not proof.
      await entry.fill(other);
      await read.click();
      await expect(panel.getByTestId('device-permit-empty')).toContainText('This is not proof that no offline permission is outstanding');
      await expect(panel.getByTestId('device-permit-device')).toHaveCount(0);
      await expect(page.getByText('Counter Shop')).toHaveCount(0);
      await page.screenshot({ path: info.outputPath('no-evidence.png'), fullPage: true });

      // Capability: a Billing-only session never renders the panel and never asks for device permission.
      capabilities = ['billing'];
      const before = reads.length;
      await page.reload();
      await page.getByLabel('Billing account ID').fill(account);
      await page.getByRole('button', { name: 'Read access status' }).click();
      await expect(page.getByText('Counter Shop').first()).toBeVisible();
      await expect(page.getByTestId('device-permit-panel')).toHaveCount(0);
      expect(reads.length).toBe(before);
      expect(problems).toEqual([]);
    } finally {
      await server.close();
    }
  });
}
