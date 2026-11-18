import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';
import { startStaticServer } from './static-server';

const accountId = 'a1111111-1111-4111-8111-111111111111';
const shopId = 'b1111111-1111-4111-8111-111111111111';
const otherShopId = 'c1111111-1111-4111-8111-111111111111';
const sessionId = 'd1111111-1111-4111-8111-111111111111';
const customerExplanation = 'Contact support about access.';
const internalReason = 'Private simulated review evidence.';
const shops = [
  { shopId, shopName: 'Indiranagar Shop', coverageMode: 'Independent' },
  { shopId: otherShopId, shopName: 'Jayanagar Shop', coverageMode: 'Independent' },
];

for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
  test(`suspension impact and acknowledgement at ${viewport.width}px`, async ({ page }, testInfo) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    const status = {
      id: 'e1111111-1111-4111-8111-111111111111', accountId, shopId: null, version: 1,
      recordedAt: '2027-02-10T00:00:00.123456Z', effectiveAt: '2027-02-10T00:00:00.123456Z',
      customerExplanation, mode: 'Security',
    };
    const state = (propagation: string) => ({ ...status, propagation, shops: shops.map((shop, index) => ({
      ...shop, accountRevision: 8, shopRevision: 10 + index, status: propagation,
    })) });
    let submitted = 0;
    let acknowledged = false;
    try {
      await page.setViewportSize(viewport);
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId: 'a2222222-2222-4222-8222-222222222222', sessionId,
        capabilities: ['security-administrator'], accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/auth/sign-in/context', route => route.fulfill({ json: { requestToken: 'fixture-csrf' } }));
      await page.route(url => url.pathname.startsWith('/api/admin/accounts/') && url.pathname.includes('/restrictions'), async route => {
        const request = route.request();
        expect(request.headers()['x-admin-session']).toBe(sessionId);
        if (request.method() === 'GET') {
          await route.fulfill({ json: submitted ? [state(acknowledged ? 'Acknowledged' : 'Pending')] : [] });
          return;
        }
        const body = request.postDataJSON();
        expect(request.headers()['x-admin-csrf']).toBe('fixture-csrf');
        if (new URL(request.url()).pathname.endsWith('/preview')) {
          expect(body.shopId).toBe(body.scope === 'Shop' ? shopId : null);
          await route.fulfill({ json: {
            accountId, accountName: 'Bengaluru Retail', selection: body, version: 0,
            alreadySuspended: false, impactDigest: '8'.repeat(64), shops: body.scope === 'Shop' ? shops.slice(0, 1) : shops,
          } });
          return;
        }
        submitted++;
        expect(body).toMatchObject({ scope: 'Account', shopId: null, mode: 'Security', expectedVersion: 0,
          effectiveAt: null, customerExplanation, internalReason, reviewReference: 'CASE-878', impactDigest: '8'.repeat(64) });
        await route.fulfill({ status: 201, json: state('Pending') });
      });

      await page.goto(server.origin + '/');
      await page.getByRole('link', { name: 'Operational restrictions' }).click();
      await page.getByLabel('Billing account ID').fill(accountId);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByRole('alert')).toContainText('Select account scope');
      await page.getByLabel('Suspension scope').selectOption('Shop');
      await page.getByLabel('Shop ID', { exact: true }).fill(shopId);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByText('Indiranagar Shop (Independent)')).toBeVisible();
      await expect(page.getByText('Jayanagar Shop (Independent)')).toHaveCount(0);
      await page.getByLabel('Suspension scope').selectOption('Account');
      await expect(page.getByRole('heading', { name: 'Affected shops' })).toHaveCount(0);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByText('Jayanagar Shop (Independent)')).toBeVisible();
      await page.getByLabel('Suspension mode').selectOption('Security');
      await page.getByLabel('Customer explanation').fill(customerExplanation);
      await page.getByRole('button', { name: 'Apply suspension' }).click();
      await expect(page.getByRole('alert')).toContainText('Complete the mode');
      expect(submitted).toBe(0);
      await page.getByLabel('Internal review reason').fill(internalReason);
      await page.getByLabel('Review reference').fill('CASE-878');
      await page.screenshot({ path: testInfo.outputPath('review.png'), fullPage: true });

      await page.getByRole('button', { name: 'Apply suspension' }).click();
      await expect(page.getByRole('status')).toHaveText('Pending merchant acknowledgement');
      await expect(page.getByText(/Account revision 8, shop revision 11/)).toBeVisible();
      await expect(page.getByText(internalReason)).toHaveCount(0);
      await page.screenshot({ path: testInfo.outputPath('pending.png'), fullPage: true });
      acknowledged = true;
      await page.getByRole('button', { name: 'Refresh saved restrictions' }).click();
      await expect(page.getByRole('status')).toHaveText('Acknowledged by merchant service');
      await expect(page.getByText(/Account revision 8, shop revision 11/)).toBeVisible();
      expect(submitted).toBe(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width);
      await page.screenshot({ path: testInfo.outputPath('acknowledged.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}
