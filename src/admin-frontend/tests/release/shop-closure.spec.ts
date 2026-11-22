import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { startStaticServer } from './static-server';

const account = 'a1111111-1111-4111-8111-111111111111';
const other = 'a2222222-2222-4222-8222-222222222222';
const adminId = 'a3333333-3333-4333-8333-333333333333';
const sessionId = 'a4444444-4444-4444-8444-444444444444';
const shopId = 'a5555555-5555-4555-8555-555555555555';
const at = '2026-11-21T04:00:00Z';

// The provider session and HTTP facts are fixtures; the Angular assets are the actual production build.
for (const width of [1440, 390]) {
  test(`provider closure evidence and private-field boundaries, ${width}px`, async ({ page }, info) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    let applied = false;
    let hold = false;
    let release: () => void = () => {};
    const held = new Promise<void>(done => { release = done; });
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId, sessionId, capabilities: ['support'], accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/accounts/*/status', route => route.fulfill({ json: {
        evaluatedAt: at, items: [], nextCursor: null,
      } }));
      await page.route('**/api/admin/accounts/*/closures', async route => {
        const request = route.request();
        expect(request.method()).toBe('GET');
        if (new URL(request.url()).pathname.includes(other)) return route.fulfill({ json: [] });
        if (hold) await held;
        await route.fulfill({ json: [{
          id: account, scope: 'Account', savedAt: at, financialTreatment: 'NoFinancialChange',
          shops: [{ shopId, shopName: 'Termless Shop', propagation: applied ? 'Applied' : 'Pending',
            savedShopRevision: 5, appliedShopRevision: applied ? 5 : null, acknowledgedAt: applied ? at : null },
          { shopId: other, shopName: 'Review Shop', propagation: 'NeedsReview', savedShopRevision: 6,
            appliedShopRevision: null, acknowledgedAt: null }],
          internalReason: 'private-fixture', sourceCount: 100005, deviceId: 'private-device',
        }] }).catch(() => {});
      });
      await page.goto(server.origin + '/accounts');
      const entry = page.getByLabel('Billing account ID');
      const read = page.getByRole('button', { name: 'Read access status' });
      await entry.fill(account);
      await read.click();
      const detail = page.getByTestId('provider-closure-detail');
      await expect(detail.getByText('Termless Shop')).toBeVisible();
      await expect(detail.getByTestId('provider-closure-propagation')).toHaveText([
        'Pending merchant acknowledgement', 'Needs review',
      ]);
      await expect(detail.getByText(/Subscriptions, payments and refunds remain unchanged/)).toBeVisible();
      await expect(detail.getByText(/private-fixture|private-device|100005/)).toHaveCount(0);
      await expect(detail.getByRole('button')).toHaveCount(0);
      await page.screenshot({ path: info.outputPath('pending-and-review.png'), fullPage: true });

      applied = true;
      await read.click();
      await expect(detail.getByTestId('provider-closure-propagation')).toHaveText([
        'Acknowledged by merchant service', 'Needs review',
      ]);
      await expect(detail).toContainText('does not establish enforcement on disconnected devices');
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('acknowledged.png'), fullPage: true });

      hold = true;
      await read.click();
      await expect(page.getByText('This account has no subscription terms.')).toBeVisible();
      await entry.fill(other);
      await read.click();
      await expect(detail).toContainText('No operational closures are recorded');
      release();
      await expect(detail.getByText('Termless Shop')).toHaveCount(0);
    } finally {
      release();
      await server.close();
    }
  });
}
